﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rhetos.Logging;

namespace Rhetos.Utilities
{
    public class ParallelTopologicalJob
    {
        private class JobTask
        {
            public string Id { get; }
            public Action Action { get; }
            public List<string> Dependencies { get; }

            public JobTask(string id, Action action, IEnumerable<string> dependencies)
            {
                Id = id;
                Action = action;
                Dependencies = dependencies.ToList();
            }

            public string DependenciesInfo() =>
                string.Join(", ", Dependencies.Select(dependency => $"'{dependency}'"));
        }

        private readonly List<JobTask> _tasks = new List<JobTask>();
        private readonly ILogger _logger;
        private readonly ILogger _performanceLogger;

        public ParallelTopologicalJob(ILogProvider logProvider)
        {
            _logger = logProvider.GetLogger(nameof(ParallelTopologicalJob));
            _performanceLogger = logProvider.GetLogger("Performance." + nameof(ParallelTopologicalJob));
        }

        public ParallelTopologicalJob AddTask(string id, Action action, IEnumerable<string> dependencies = null)
        {
            if (_tasks.Any(task => task.Id == id))
                throw new InvalidOperationException($"Task with id '{id}' has already been added to the job.");

            _tasks.Add(new JobTask(id, action, dependencies ?? Enumerable.Empty<string>()));
            return this;
        }

        public void RunAllTasks(int maxDegreeOfParallelism = -1, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            var runningTasks = new Dictionary<string, Task>();
            var completedTasks = new Dictionary<string, Task>();
            var anyFaulted = false;

            while (completedTasks.Count < _tasks.Count)
            {
                var maxNewTasksAllowed = maxDegreeOfParallelism > 0
                    ? maxDegreeOfParallelism - runningTasks.Count
                    : _tasks.Count;

                // start new eligible tasks
                if (maxNewTasksAllowed > 0 && !anyFaulted)
                {
                    var eligibleTasks = _tasks.Where(task =>
                            !runningTasks.ContainsKey(task.Id)
                            && !completedTasks.ContainsKey(task.Id)
                            && task.Dependencies.All(dependency => completedTasks.ContainsKey(dependency)))
                        .Take(maxNewTasksAllowed)
                        .ToList();

                    if (runningTasks.Count == 0 && eligibleTasks.Count == 0)
                    {
                        var invalidTasks = _tasks.Where(task => !completedTasks.ContainsKey(task.Id));
                        var invalidTaskReasons = invalidTasks
                            .Select(task => $"task '{task.Id}' requires {task.DependenciesInfo()}");
                        throw new InvalidOperationException($"Unable to resolve required task dependencies ({string.Join("; ", invalidTaskReasons)}).");
                    }

                    foreach (var eligibleTask in eligibleTasks)
                        runningTasks.Add(eligibleTask.Id, Task.Run(() => RunSingleTask(eligibleTask), cancellationToken));
                }

                Task.WaitAny(runningTasks.Values.ToArray(), cancellationToken);

                // collect some of the completed tasks and process them
                // due to race condition further processing might miss some completed tasks - they will be handled in the next iteration
                var newlyCompletedTasks = runningTasks
                    .Where(a => a.Value.IsCompleted)
                    .ToList();

                foreach (var task in newlyCompletedTasks)
                {
                    completedTasks.Add(task.Key, task.Value);
                    runningTasks.Remove(task.Key);
                }

                anyFaulted = completedTasks.Values.Any(task => task.IsFaulted);
                if (anyFaulted && runningTasks.Count == 0)
                    break;
            }

            ThrowIfAnyTaskErrors(completedTasks);
            _performanceLogger.Write(sw, () => $"Executed {_tasks.Count} tasks.");
        }

        private void ThrowIfAnyTaskErrors(Dictionary<string, Task> completedTasks)
        {
            var errors = completedTasks.Values
                .Where(task => task.IsFaulted)
                .Select(task => task.Exception?.InnerException ?? task.Exception)
                .ToList();

            if (errors.Any())
                throw new AggregateException(errors);
        }

        private void RunSingleTask(JobTask task)
        {
            var sw = Stopwatch.StartNew();
            _logger.Trace(() => $"Starting '{task.Id}', dependencies: {task.DependenciesInfo()}.");

            task.Action();

            _performanceLogger.Write(sw, () => $"Task '{task.Id}' completed.");
        }
    }
}
