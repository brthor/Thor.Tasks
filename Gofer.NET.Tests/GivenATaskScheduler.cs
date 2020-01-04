using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NCrontab;
using Newtonsoft.Json.Bson;
using Xunit;

namespace Gofer.NET.Tests
{
    public class GivenATaskScheduler
    {
        private int IntervalSeconds => 2;

        private string RandomTaskName = Guid.NewGuid().ToString();

        [Fact]
        public async Task ItExecutesAScheduledTaskAtTheSpecifiedOffsetOnlyOnce()
        {
            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                await taskScheduler.AddScheduledTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    TimeSpan.FromSeconds(IntervalSeconds));
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerWritesSemaphoreOnlyOnce(
                IntervalSeconds,
                configureSchedulerAction);
        }

        [Fact]
        public async Task ItExecutesAScheduledTaskAtTheSpecifiedDateTimeOffsetOnlyOnce()
        {
            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                await taskScheduler.AddScheduledTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    new DateTimeOffset(DateTime.UtcNow + TimeSpan.FromSeconds(IntervalSeconds), TimeSpan.FromHours(0)));
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerWritesSemaphoreOnlyOnce(
                IntervalSeconds,
                configureSchedulerAction);
        }

        [Fact]
        public async Task ItExecutesAScheduledTaskAtTheSpecifiedDateTimeOnlyOnce()
        {
            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                await taskScheduler.AddScheduledTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    DateTime.UtcNow + TimeSpan.FromSeconds(IntervalSeconds));
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerWritesSemaphoreOnlyOnce(
                IntervalSeconds,
                configureSchedulerAction);
        }

        [Fact]
        public async Task ItExecutesARecurringTaskAtTheSpecifiedInterval()
        {
            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    TimeSpan.FromSeconds(IntervalSeconds), RandomTaskName);
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerWritesSemaphoreTwice(
                IntervalSeconds,
                configureSchedulerAction);
        }

        [Fact]
        public async Task ItExecutesARecurringTaskAtTheSpecifiedCrontabInterval()
        {
            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                var crontab = $"*/{IntervalSeconds} * * * * *";
                TaskSchedulerTestHelpers.SynchronizeToCrontabNextStart(crontab);
                Thread.Sleep(10);

                await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    crontab, RandomTaskName);
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerWritesSemaphoreTwice(IntervalSeconds, 
                configureSchedulerAction);
        }

        [Fact]
        public async Task ItAllowsForRecurringTaskCrontabSchedulesToBeChanged()
        {
            var taskName = Guid.NewGuid().ToString();

            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                var crontab = $"*/{IntervalSeconds} * * * * *";
                TaskSchedulerTestHelpers.SynchronizeToCrontabNextStart(crontab);
                Thread.Sleep(10);

                await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    crontab, taskName);
            };

            Func<string, TaskScheduler, Task> reconfigureRecurringTaskIntervalAction = async (semaphoreFile, taskScheduler) => 
            {
                // Must synchronize to next start time or test becomes flaky
                var newCrontab = $"*/{IntervalSeconds*2} * * * * *";
                TaskSchedulerTestHelpers.SynchronizeToCrontabNextStart(newCrontab);
                Thread.Sleep(10);

                await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    newCrontab, taskName);
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerWritesSemaphoreTwiceAfterReconfiguringInterval(
                IntervalSeconds,
                configureSchedulerAction,
                reconfigureRecurringTaskIntervalAction);
        }

        [Fact]
        public async Task ItAllowsForRecurringTaskTimespanSchedulesToBeChanged()
        {
            var taskName = Guid.NewGuid().ToString();

            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    TimeSpan.FromSeconds(IntervalSeconds), taskName);
            };

            Func<string, TaskScheduler, Task> reconfigureRecurringTaskIntervalAction = async (semaphoreFile, taskScheduler) => 
            {
                await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    TimeSpan.FromSeconds(IntervalSeconds*2), taskName);
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerWritesSemaphoreTwiceAfterReconfiguringInterval(
                IntervalSeconds,
                configureSchedulerAction,
                reconfigureRecurringTaskIntervalAction);
        }

        [Fact]
        public async Task ItAllowsForRecurringTasksTaskInfoToBeChanged()
        {
            var taskName = Guid.NewGuid().ToString();
            var taskQueue = TaskQueueTestFixture.UniqueRedisTaskQueue();
            var taskScheduler = new TaskScheduler(taskQueue);

            var recurringTask = await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore("afile"),
                    $"*/{IntervalSeconds} * * * * *", taskName);

            var fetchedRecurringTask = await taskScheduler.GetRecurringTask(recurringTask.TaskKey);
            fetchedRecurringTask.TaskInfo.MethodName.Should().Be("WriteSemaphore");
            fetchedRecurringTask.Interval.Should().BeNull();
            fetchedRecurringTask.Crontab.Should().Be($"*/{IntervalSeconds} * * * * *");
            fetchedRecurringTask.TaskInfo.Args[0].Should().Be("afile");

            // Different Argument
            await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore("bfile"),
                    $"*/{IntervalSeconds} * * * * *", taskName);

            fetchedRecurringTask = await taskScheduler.GetRecurringTask(recurringTask.TaskKey);
            fetchedRecurringTask.TaskInfo.MethodName.Should().Be("WriteSemaphore");
            fetchedRecurringTask.Interval.Should().BeNull();
            fetchedRecurringTask.Crontab.Should().Be($"*/{IntervalSeconds} * * * * *");
            fetchedRecurringTask.TaskInfo.Args[0].Should().Be("bfile");

            // Different Interval
            await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore("bfile"),
                    TimeSpan.FromSeconds(IntervalSeconds), taskName);

            fetchedRecurringTask = await taskScheduler.GetRecurringTask(recurringTask.TaskKey);
            fetchedRecurringTask.TaskInfo.MethodName.Should().Be("WriteSemaphore");
            fetchedRecurringTask.Interval.Should().Be(TimeSpan.FromSeconds(IntervalSeconds));
            fetchedRecurringTask.Crontab.Should().BeNull();
            fetchedRecurringTask.TaskInfo.Args[0].Should().Be("bfile");

            // Different Target Method
            await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphoreValue("bfile", "avalue"),
                    TimeSpan.FromSeconds(IntervalSeconds), taskName);

            fetchedRecurringTask = await taskScheduler.GetRecurringTask(recurringTask.TaskKey);
            fetchedRecurringTask.TaskInfo.MethodName.Should().Be("WriteSemaphoreValue");
            fetchedRecurringTask.Interval.Should().Be(TimeSpan.FromSeconds(IntervalSeconds));
            fetchedRecurringTask.Crontab.Should().BeNull();
            fetchedRecurringTask.TaskInfo.Args[0].Should().Be("bfile");
            fetchedRecurringTask.TaskInfo.Args[1].Should().Be("avalue");
        }

        [Fact]
        public async Task ItAllowsForRecurringTasksToBeCanceled()
        {
            var taskName = Guid.NewGuid().ToString();
            RecurringTask recurringTask = null;

            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                recurringTask = await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    $"*/{IntervalSeconds} * * * * *", taskName);
            };

            Func<TaskScheduler, Task> cancelTaskAction = async (taskScheduler) => 
            {
                (await taskScheduler.CancelRecurringTask(recurringTask)).Should().BeTrue();
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerDoesNotWriteSemaphore(
                IntervalSeconds,
                configureSchedulerAction,
                cancelTaskAction);
        }

        [Fact]
        public async Task ItAllowsForScheduledTasksToBeCanceled()
        {
            var taskName = Guid.NewGuid().ToString();
            ScheduledTask scheduledTask = null;

            Func<string, TaskScheduler, Task> configureSchedulerAction = async (semaphoreFile, taskScheduler) =>
            {
                scheduledTask = await taskScheduler.AddScheduledTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    DateTime.UtcNow + TimeSpan.FromSeconds(IntervalSeconds));
            };

            Func<TaskScheduler, Task> cancelTaskAction = async (taskScheduler) => 
            {
                (await taskScheduler.CancelScheduledTask(scheduledTask)).Should().BeTrue();
            };

            await TaskSchedulerTestHelpers.AssertTaskSchedulerDoesNotWriteSemaphore(
                IntervalSeconds,
                configureSchedulerAction,
                cancelTaskAction);
        }

        [Fact]
        public async Task ItDoesNotBlowUpWhenCancelingNonExistentTasks()
        {
            var taskQueue = TaskQueueTestFixture.UniqueRedisTaskQueue();
            var taskScheduler = new TaskScheduler(taskQueue);

            (await taskScheduler.CancelTask(new TaskKey("hello"))).Should().BeFalse();
        }

        [Fact]
        public async Task ItReturnsNullWhenGettingNonExistentRecurringTasks()
        { 
            var taskQueue = TaskQueueTestFixture.UniqueRedisTaskQueue();
            var taskScheduler = new TaskScheduler(taskQueue);
            
            (await taskScheduler.GetRecurringTask("hello")).Should().BeNull();
        }

        [Fact]
        public async Task ItDoesNotOverwriteOriginalRecurringTaskWhenDuplicatesAreAdded()
        {
            var taskName = Guid.NewGuid().ToString();
            var taskQueue = TaskQueueTestFixture.UniqueRedisTaskQueue();
            var taskScheduler = new TaskScheduler(taskQueue);

            var originalRecurringTask = await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore("a"),
                    TimeSpan.FromSeconds(IntervalSeconds), taskName);

            var duplicateRecurringTask = await taskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore("a"),
                    TimeSpan.FromSeconds(IntervalSeconds), taskName);

            originalRecurringTask.Should().NotBeNull();
            duplicateRecurringTask.Should().BeNull();

            var recurringTask = await taskScheduler.GetRecurringTask(originalRecurringTask.TaskKey);
            recurringTask.StartTime.Should().Be(originalRecurringTask.StartTime);
        }

        [Theory]
        [InlineData("scheduled")]
        [InlineData("recurring")]
        public async Task ItExecutesTasksOnlyOnceWhenUsingMultipleConsumers(string taskType)
        {
            var semaphoreFile = Path.GetTempFileName();
            File.Delete(semaphoreFile);
            File.Create(semaphoreFile).Close();
            
            var intervalSeconds = 5;
            var taskQueue = TaskQueueTestFixture.UniqueRedisTaskQueue();

            var schedulingTaskScheduler = new TaskScheduler(taskQueue);
            
            var taskSchedulers = new[]
            {
                new TaskScheduler(taskQueue),
                new TaskScheduler(taskQueue),
                new TaskScheduler(taskQueue),
                new TaskScheduler(taskQueue),
            };

            if (taskType == "scheduled")
            {
                await schedulingTaskScheduler.AddScheduledTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    TimeSpan.FromSeconds(intervalSeconds));
            }

            if (taskType == "recurring")
            {
                await schedulingTaskScheduler.AddRecurringTask(() => TaskQueueTestFixture.WriteSemaphore(semaphoreFile),
                    TimeSpan.FromSeconds(intervalSeconds), RandomTaskName);
            }
            
            Thread.Sleep(((intervalSeconds) * 1000) + 50);

            // Ran only once
            await Task.WhenAll(
                taskSchedulers.Select(
                    taskScheduler =>
                        Task.Run(async () =>
                        {
                            await taskScheduler.Tick();
                            await taskQueue.ExecuteNext();
                        })));
            
            File.Exists(semaphoreFile).Should().Be(true);
            File.ReadAllText(semaphoreFile).Should().Be(TaskQueueTestFixture.SemaphoreText);
            
            
            // Ran only twice (or once if scheduled)
            Thread.Sleep(((intervalSeconds) * 1000) + 50);
            await Task.WhenAll(
                taskSchedulers.Select(
                    taskScheduler =>
                        Task.Run(async () =>
                        {
                            await taskScheduler.Tick();
                            await taskQueue.ExecuteNext();
                        })));

            if (taskType == "recurring")
            {
                File.ReadAllText(semaphoreFile).Should()
                    .Be(TaskQueueTestFixture.SemaphoreText + TaskQueueTestFixture.SemaphoreText);
            }
            if (taskType == "scheduled")
            {
                File.ReadAllText(semaphoreFile).Should()
                    .Be(TaskQueueTestFixture.SemaphoreText);
            }
        }
    }
}