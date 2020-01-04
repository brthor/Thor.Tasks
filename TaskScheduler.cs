using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Gofer.NET.Utils;
using StackExchange.Redis;

namespace Gofer.NET
{
    public class TaskScheduler
    {
        private string TaskBackendKeySetPersistenceKey => 
            $"{nameof(TaskScheduler)}::{nameof(TaskBackendKeySetPersistenceKey)}::{_taskQueue.Config.QueueName}";

        private string ScheduledTasksOrderedSetKey => 
            $"{nameof(TaskScheduler)}::{nameof(ScheduledTasksOrderedSetKey)}::{_taskQueue.Config.QueueName}";

        private string ScheduledTasksMapKey => 
            $"{nameof(TaskScheduler)}::{nameof(ScheduledTasksMapKey)}::{_taskQueue.Config.QueueName}";

        private string ScheduledTaskPromotionLockKey => 
            $"{nameof(TaskScheduler)}::{nameof(ScheduledTaskPromotionLockKey)}::{_taskQueue.Config.QueueName}";

        private string RecurringTaskRescheduleQueueKey => 
            $"{nameof(TaskScheduler)}::{nameof(RecurringTaskRescheduleQueueKey)}::{_taskQueue.Config.QueueName}";

        private LoadedLuaScript LoadedScheduledTaskPromotionScript { get; set; }

        private LoadedLuaScript LoadedRescheduleRecurringTasksScript { get; set; }

        private LoadedLuaScript LoadedCancelTaskScript { get; set; }

        private readonly TaskQueue _taskQueue;

        private readonly TimeSpan _promotionFrequency;

        private DateTime _lastPromotionRunTime;

        public TaskScheduler(TaskQueue taskQueue, TimeSpan? updateFrequency=null)
        {
            _taskQueue = taskQueue;
            _promotionFrequency = updateFrequency ?? TimeSpan.FromMilliseconds(100);
        }

        public async Task Tick(bool forceRunPromotion=false)
        {
            if (forceRunPromotion || ShouldRunScheduledTaskPromotion()) 
            {
                // Update the last run time whether or not we get the lock.
                _lastPromotionRunTime = DateTime.UtcNow;

                var promotionLock = await _taskQueue.Backend.LockNonBlocking(ScheduledTaskPromotionLockKey);
                if (promotionLock != null)
                {
                    try 
                    {
                        await PromoteDueScheduledTasks();
                    }
                    finally
                    {
                        await promotionLock.Release();
                    }
                }
            }

            await RescheduleRecurringTasks();
        }

        public async Task<ScheduledTask> AddScheduledTask(Expression<Action> action, TimeSpan offsetFromNow)
        {
            var scheduledTask = new ScheduledTask(action.ToTaskInfo(), offsetFromNow, TaskKey.CreateUnique());

            await EnqueueScheduledTask(scheduledTask);

            return scheduledTask;
        }

        public async Task<ScheduledTask> AddScheduledTask(Expression<Action> action, DateTimeOffset scheduledTime)
        {
            var scheduledTask = new ScheduledTask(action.ToTaskInfo(), scheduledTime, TaskKey.CreateUnique());

            await EnqueueScheduledTask(scheduledTask);

            return scheduledTask;
        }

        public async Task<ScheduledTask> AddScheduledTask(Expression<Action> action, DateTime scheduledTime)
        {
            var scheduledTask = new ScheduledTask(action.ToTaskInfo(), scheduledTime, TaskKey.CreateUnique());

            await EnqueueScheduledTask(scheduledTask);

            return scheduledTask;
        }

        public async Task<RecurringTask> AddRecurringTask(Expression<Action> action, TimeSpan interval, string taskName)
        {
            var recurringTask = new RecurringTask(action.ToTaskInfo(), interval, 
                TaskKey.CreateRecurring(taskName));

            if (await RecurringTaskDoesNotExistOrNeedsChange(recurringTask)) 
            {
                await EnqueueRecurringTask(recurringTask);
                return recurringTask;
            }

            return null;
        }

        public async Task<RecurringTask> AddRecurringTask(Expression<Action> action, string crontab, string taskName)
        {
            var recurringTask = new RecurringTask(action.ToTaskInfo(), crontab, 
                TaskKey.CreateRecurring(taskName));

            if (await RecurringTaskDoesNotExistOrNeedsChange(recurringTask)) 
            {
                await EnqueueRecurringTask(recurringTask);
            }

            return recurringTask;
        }

        public async Task<bool> CancelRecurringTask(RecurringTask recurringTask) 
        {
            return await CancelTask(recurringTask.TaskKey);
        }

        public async Task<bool> CancelRecurringTask(string taskName) 
        {
            return await CancelTask(TaskKey.CreateRecurring(taskName));
        }

        public async Task<bool> CancelScheduledTask(ScheduledTask scheduledTask) 
        {
            return await CancelTask(scheduledTask.TaskKey);
        }

        public async Task<bool> CancelTask(TaskKey taskKey)
        {
            if (LoadedCancelTaskScript == null)
            {
                LoadedCancelTaskScript = await _taskQueue.Backend.LoadLuaScript(LuaScriptToCancelTask());
            }

            var didCancel = await _taskQueue.Backend.RunLuaScript(LoadedCancelTaskScript, 
                new [] {
                    (RedisKey) ScheduledTasksOrderedSetKey,
                    (RedisKey) ScheduledTasksMapKey,
                },
                new [] {
                    (RedisValue) taskKey.Value
                });

            return (bool) didCancel;
        }

        public async Task<RecurringTask> GetRecurringTask(TaskKey taskKey)
        {
            var serializedRecurringTask = await _taskQueue.Backend.GetMapField(ScheduledTasksMapKey, 
                $"serializedRecurringTask::{taskKey.Value}");

            if (string.IsNullOrEmpty(serializedRecurringTask))
                return null;
            
            var recurringTask = JsonTaskInfoSerializer.Deserialize<RecurringTask>(serializedRecurringTask);

            return recurringTask;
        }

        public async Task<RecurringTask> GetRecurringTask(string taskKey)
        {
            var serializedRecurringTask = await _taskQueue.Backend.GetMapField(ScheduledTasksMapKey, 
                $"serializedRecurringTask::{taskKey}");

            if (string.IsNullOrEmpty(serializedRecurringTask))
                return null;
            
            var recurringTask = JsonTaskInfoSerializer.Deserialize<RecurringTask>(serializedRecurringTask);

            return recurringTask;
        }

        private async Task<bool> RecurringTaskDoesNotExistOrNeedsChange(RecurringTask recurringTask)
        {
            var deserializedRecurringTask = await GetRecurringTask(recurringTask.TaskKey);
            if (deserializedRecurringTask == null)
            {
                return true;
            }

            if (!recurringTask.IsEquivalent(deserializedRecurringTask))
            {
                return true;
            }

            return false;
        }

        private bool ShouldRunScheduledTaskPromotion() 
        {
            if (_lastPromotionRunTime == null) 
            {
                return true;
            }

            var timeSinceLastUpdate = DateTime.UtcNow - _lastPromotionRunTime;

            if (timeSinceLastUpdate > _promotionFrequency) 
            {
                return true;
            }

            return false;
        }

        private async Task EnqueueRecurringTask(RecurringTask recurringTask)
        {
            var serializedTaskInfo = JsonTaskInfoSerializer.Serialize(recurringTask.TaskInfo);
            await _taskQueue.Backend.SetMapFields(ScheduledTasksMapKey, 
                (recurringTask.TaskKey.Value, serializedTaskInfo),
                ($"isRecurring::{recurringTask.TaskKey}", true),
                ($"serializedRecurringTask::{recurringTask.TaskKey}", JsonTaskInfoSerializer.Serialize(recurringTask)));

            var nextRunTimestamp = recurringTask.GetNextRunTimestamp(recurringTask.StartTime);

            await _taskQueue.Backend.AddToOrderedSet(
                ScheduledTasksOrderedSetKey, 
                nextRunTimestamp, 
                recurringTask.TaskKey.Value);
        }

        private async Task EnqueueScheduledTask(ScheduledTask scheduledTask)
        {
            var serializedTaskInfo = JsonTaskInfoSerializer.Serialize(scheduledTask.TaskInfo);
            
            await _taskQueue.Backend.SetMapFields(ScheduledTasksMapKey, 
                (scheduledTask.TaskKey.Value, serializedTaskInfo),
                ($"isRecurring::{scheduledTask.TaskKey}", false));

            await _taskQueue.Backend.AddToOrderedSet(
                ScheduledTasksOrderedSetKey, 
                scheduledTask.ScheduledUnixTimeMilliseconds, 
                scheduledTask.TaskKey.Value);
        }

        private async Task RescheduleRecurringTasks()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            RecurringTask recurringTask;
            long nextRunTimestamp;

            if (LoadedRescheduleRecurringTasksScript == null)
            {
                LoadedRescheduleRecurringTasksScript = await _taskQueue.Backend.LoadLuaScript(LuaScriptToRescheduleRecurringTask());
            }

            var serializedRecurringTasks = (await _taskQueue.Backend
                .DequeueBatch(RecurringTaskRescheduleQueueKey)).ToArray();

            if (serializedRecurringTasks.Length == 0)
            {
                return;
            }

            var args = new List<RedisValue>();

            foreach (var serializedRecurringTask in serializedRecurringTasks)
            {
                recurringTask = JsonTaskInfoSerializer.Deserialize<RecurringTask>(serializedRecurringTask);
                nextRunTimestamp = recurringTask.GetNextRunTimestamp(DateTime.UtcNow);

                args.Add((RedisValue) recurringTask.TaskKey.Value);
                args.Add((RedisValue) nextRunTimestamp);
            }

            await _taskQueue.Backend.RunLuaScript(LoadedRescheduleRecurringTasksScript, 
                new [] {
                    (RedisKey) ScheduledTasksOrderedSetKey,
                    (RedisKey) ScheduledTasksMapKey,
                },
                args.ToArray());
            

            var profile = $"PROFILE {nameof(RescheduleRecurringTasks)}: {sw.ElapsedMilliseconds}";
            // Console.WriteLine(profile);
        }

        private async Task PromoteDueScheduledTasks() 
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (LoadedScheduledTaskPromotionScript == null)
            {
                LoadedScheduledTaskPromotionScript = await _taskQueue.Backend.LoadLuaScript(LuaScriptToPromoteScheduledTasks());
            }

            await _taskQueue.Backend.RunLuaScript(LoadedScheduledTaskPromotionScript, 
                new [] {
                    (RedisKey) ScheduledTasksOrderedSetKey,
                    (RedisKey) ScheduledTasksMapKey,
                    (RedisKey) _taskQueue.Config.QueueName,
                    (RedisKey) RecurringTaskRescheduleQueueKey
                },
                new [] {(RedisValue) DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});

            var profile = $"PROFILE {nameof(PromoteDueScheduledTasks)}: {sw.ElapsedMilliseconds}";
            // Console.WriteLine(profile);
        }

        private string LuaScriptToPromoteScheduledTasks() 
        {
            // KEYS[1] = Key for scheduled tasks sorted set
            // KEYS[2] = Key for scheduled tasks task info hash
            // KEYS[3] = Key for task queue
            // KEYS[4] = Key for recurring task re-schedule list
            // ARGV[1] = Current unix timestamp (milliseconds)
            return @"
                local dueScheduledTasks = redis.call(""ZREVRANGEBYSCORE"", KEYS[1], ARGV[1], 0)
                
                local serializedTaskInfo
                local isRecurring
                local serializedRecurringJob
                local key
                local values
                
                for i = #dueScheduledTasks, 1, -1 do
                    key = dueScheduledTasks[i]
                    values = redis.call(""HMGET"", KEYS[2], key, ""isRecurring::""..key, ""serializedRecurringTask::""..key)
                    serializedTaskInfo = values[1]
                    isRecurring = values[2]
                    serializedRecurringJob = values[3]

                    redis.call(""LPUSH"", KEYS[3], serializedTaskInfo)

                    if (isRecurring == ""1"") then
                        redis.call(""LPUSH"", KEYS[4], serializedRecurringJob)
                    else
                        redis.call(""HDEL"", KEYS[2], key, ""isRecurring::""..key)
                    end
                end

                redis.call(""ZREMRANGEBYSCORE"", KEYS[1], 0, ARGV[1])
                ";
        }

        private string LuaScriptToRescheduleRecurringTask() 
        {
            // KEYS[1] = Key for scheduled tasks sorted set
            // KEYS[2] = Key for scheduled tasks task info hash
            // ARGV[1] = Recurring Task TaskKey
            // ARGV[2] = Recurring Task next run timestamp (milliseconds)
            return @"
                for i=1, #ARGV do
                    local recurringTaskStillScheduled = redis.call(""HGET"", KEYS[2], ARGV[i])

                    if (recurringTaskStillScheduled) then
                        redis.call(""ZADD"", KEYS[1], ARGV[i+1], ARGV[i])
                    end
                end
                ";
        }

        private string LuaScriptToCancelTask()
        {
            // KEYS[1] = Key for scheduled tasks sorted set
            // KEYS[2] = Key for scheduled tasks task info hash
            // ARGV[1] = Task TaskKey
            return @"
                local deletedSchedule = redis.call(""ZREM"", KEYS[1], ARGV[1])
                local deletedFields = redis.call(""HDEL"", KEYS[2], ARGV[1], ""isRecurring::""..ARGV[1], ""serializedRecurringTask::""..ARGV[1])

                if (deletedSchedule ~= 0) then
                    return true
                end

                if (deletedFields ~= 0) then
                    return true
                end

                return false
                ";
        }
    }
}
