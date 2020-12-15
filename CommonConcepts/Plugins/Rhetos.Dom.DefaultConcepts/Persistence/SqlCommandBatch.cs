﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Data.Common;
using Rhetos.Persistence;
using System.Text;

namespace Rhetos.Dom.DefaultConcepts
{
	public class SqlCommandBatch : IPersistanceStorageCommandBatch
	{
		private int _batchNumber;

		private List<Command> _commands;

		private IPersistenceTransaction _persistenceTransaction;

		private IPersistanceStorageObjectMappings _persistanceMappingConfiguration;

		private Action<int, DbCommand> _afterCommandExecution;

		public SqlCommandBatch(
			IPersistenceTransaction persistenceTransaction,
			IPersistanceStorageObjectMappings persistanceMappingConfiguration,
			int batchNumber,
			Action<int, DbCommand> afterCommandExecution)
		{
			_batchNumber = batchNumber;
			_commands = new List<Command>();
			_persistenceTransaction = persistenceTransaction;
			_persistanceMappingConfiguration = persistanceMappingConfiguration;
			_afterCommandExecution = afterCommandExecution;
		}

		public IPersistanceStorageCommandBatch Add<T>(T entity, PersistanceStorageCommandType commandType) where T : IEntity
		{	
			_commands.Add(new Command
			{
				Entity = entity,
				EntityType = typeof(T),
				CommandType = commandType
			});

			return this;
		}

		public int Execute()
		{
			var numberOfAffectedRows = 0;

			var commandParameters = new List<DbParameter>();
			var commandTextBuilder = new StringBuilder();
			using (var command = _persistenceTransaction.Connection.CreateCommand())
			{
				command.Transaction = _persistenceTransaction.Transaction;

				var numberOfBatchedCommand = 0;
				for (int i = 0; i < _commands.Count; i++)
				{
					AppendCommand(_commands[i], commandParameters, commandTextBuilder);
					numberOfBatchedCommand++;

					if (numberOfBatchedCommand == _batchNumber || i == _commands.Count - 1)
					{
						numberOfAffectedRows += ExecuteNonQueryAndClearCommand(command, commandParameters, commandTextBuilder);
						numberOfBatchedCommand = 0;
					}
				}
				_commands.Clear();
			}

			return numberOfAffectedRows;
		}

		private int ExecuteNonQueryAndClearCommand(DbCommand command, List<DbParameter> commandParameters, StringBuilder commandTextBuilder)
		{
			var numberOfAffectedRows = 0;
			if (commandTextBuilder.Length != 0)
			{
				command.CommandText = commandTextBuilder.ToString();
				foreach(var parameter in commandParameters)
					command.Parameters.Add(parameter);
				numberOfAffectedRows = command.ExecuteNonQuery();
				_afterCommandExecution?.Invoke(numberOfAffectedRows, command);
			}
			command.CommandText = "";
			command.Parameters.Clear();
			commandParameters.Clear();
			commandTextBuilder.Clear();
			return numberOfAffectedRows;
		}

		private void AppendCommand(Command comand, List<DbParameter> commandParameters, StringBuilder commandTextBuilder)
		{
			if (comand.CommandType == PersistanceStorageCommandType.Insert)
			{
				AppendInsertCommand(commandParameters, commandTextBuilder, comand.Entity, _persistanceMappingConfiguration.GetMapping(comand.EntityType));
			}
			if (comand.CommandType == PersistanceStorageCommandType.Update)
			{
				AppendUpdateCommand(commandParameters, commandTextBuilder, comand.Entity, _persistanceMappingConfiguration.GetMapping(comand.EntityType));
			}
			if (comand.CommandType == PersistanceStorageCommandType.Delete)
			{
				AppendDeleteCommand(commandParameters, commandTextBuilder, comand.Entity, _persistanceMappingConfiguration.GetMapping(comand.EntityType));
			}
		}

		private void AppendInsertCommand(List<DbParameter> commandParameters, StringBuilder commandTextBuilder, IEntity entity, IPersistanceStorageObjectMapper mapper)
		{
			var parameters = mapper.GetParameters(entity);
			InitializeAndAppendParameters(commandParameters, parameters);
			AppendInsertCommandTextForType(parameters, mapper.GetTableName(), commandTextBuilder);
		}

		private void InitializeAndAppendParameters(List<DbParameter> commandParameters, Dictionary<string, DbParameter> parameters)
		{
			var index = commandParameters.Count;
			foreach (var item in parameters)
			{
				item.Value.ParameterName = "@" + index;
				index++;
			}
			commandParameters.AddRange(parameters.Select(x => x.Value));
		}

		private void AppendUpdateCommand(List<DbParameter> commandParameters, StringBuilder commandTextBuilder, IEntity entity, IPersistanceStorageObjectMapper mapper)
		{
			var parameters = mapper.GetParameters(entity);
			//If parameters has only the ID property we will not execute the update command
			if (parameters.Count() > 1)
			{
				InitializeAndAppendParameters(commandParameters, parameters);
				AppendUpdateCommandTextForType(parameters, mapper.GetTableName(), commandTextBuilder);
			}
		}

		private void AppendDeleteCommand(List<DbParameter> commandParameters, StringBuilder commandTextBuilder, IEntity entity, IPersistanceStorageObjectMapper mapper)
		{
			var entityName = mapper.GetTableName();
			commandTextBuilder.Append($@"DELETE FROM {entityName} WHERE ID = '{entity.ID}';");
		}

		private void AppendInsertCommandTextForType(Dictionary<string, DbParameter> parameters, string tableFullName, StringBuilder commandTextBuilder)
		{
			commandTextBuilder.Append("INSERT INTO " + tableFullName + " (");
			foreach (var keyValue in parameters)
			{
				commandTextBuilder.Append(keyValue.Key + ", ");
			}
			commandTextBuilder.Length = commandTextBuilder.Length - 2;

			commandTextBuilder.Append(") VALUES (");

			foreach (var keyValue in parameters)
			{
				commandTextBuilder.Append(keyValue.Value.ParameterName + ", ");
			}
			commandTextBuilder.Length = commandTextBuilder.Length - 2;
			commandTextBuilder.Append(");");
		}

		private void AppendUpdateCommandTextForType(Dictionary<string, DbParameter> parameters, string tableFullName, StringBuilder commandTextBuilder)
		{
			commandTextBuilder.Append("UPDATE " + tableFullName + " SET ");
			foreach (var keyValue in parameters)
			{
				if (keyValue.Key != "ID")
				{
					commandTextBuilder.Append(keyValue.Key + " = " + keyValue.Value.ParameterName + ", ");
				}
			}
			commandTextBuilder.Length = commandTextBuilder.Length - 2;
			commandTextBuilder.Append(" WHERE ID = " + parameters["ID"] + ";");
		}

        private class Command
		{
			public IEntity Entity { get; set; }
			public Type EntityType { get; set; }
			public PersistanceStorageCommandType CommandType { get; set; }
		}
	}
}