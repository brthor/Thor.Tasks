using System;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Gofer.NET.Utils;
using Xunit;
using FluentAssertions;
using System.Reflection;

namespace Gofer.NET.Tests
{
    public class GivenARecurringTask
    {
        [Fact]
        public void ItPersistsPropertiesWhenSerializedAndDeserialized()
        {  
            var taskKey = 
                new TaskKey($"{nameof(ItPersistsPropertiesWhenSerializedAndDeserialized)}::{Guid.NewGuid().ToString()}");

            var testTask = GetTestTask(() => 
                Console.WriteLine(nameof(ItPersistsPropertiesWhenSerializedAndDeserialized)));

            var recurringTasks = new [] {
                new RecurringTask(testTask, TimeSpan.FromMinutes(5), taskKey),
                new RecurringTask(testTask, "* * * * * *", taskKey)
            };

            foreach (var recurringTask in recurringTasks) {
                var serializedRecurringTask = JsonTaskInfoSerializer.Serialize(recurringTask);
                var deserializedRecurringTask = JsonTaskInfoSerializer.Deserialize<RecurringTask>(serializedRecurringTask);

                deserializedRecurringTask.StartTime.Should().Be(recurringTask.StartTime);
                deserializedRecurringTask.TaskKey.Should().Be(recurringTask.TaskKey);
                deserializedRecurringTask.Interval.Should().Be(recurringTask.Interval);
                deserializedRecurringTask.Crontab.Should().Be(recurringTask.Crontab);
                deserializedRecurringTask.TaskInfo.IsEquivalent(recurringTask.TaskInfo).Should().BeTrue();
            }
        }

        [Fact]
        public void ItProperlyDeterminesEquivalence()
        {
            var taskInfo1 = GetTestTask(() => TestMethod1("hello world"));
            var taskInfo2 = GetTestTask(() => TestMethod2());

            var taskKey = new TaskKey($"{nameof(ItPersistsPropertiesWhenSerializedAndDeserialized)}::{Guid.NewGuid().ToString()}");

            new RecurringTask(taskInfo1, TimeSpan.FromMinutes(5), taskKey)
                .IsEquivalent(new RecurringTask(taskInfo1, TimeSpan.FromMinutes(5), taskKey))
                .Should().BeTrue("TRUE: Equivalent Timespans");

            new RecurringTask(taskInfo1, "* * * * * *", taskKey)
                .IsEquivalent(new RecurringTask(taskInfo1, "* * * * * *", taskKey))
                .Should().BeTrue("TRUE: Equivalent Crontabs");

            new RecurringTask(taskInfo1, TimeSpan.FromMinutes(5), taskKey)
                .IsEquivalent(new RecurringTask(taskInfo2, TimeSpan.FromMinutes(5), taskKey))
                .Should().BeFalse("FALSE: Different TaskInfo");

            new RecurringTask(taskInfo1, TimeSpan.FromMinutes(5), taskKey)
                .IsEquivalent(new RecurringTask(taskInfo1, TimeSpan.FromMinutes(7), taskKey))
                .Should().BeFalse("FALSE: Different Timespans");

            new RecurringTask(taskInfo1, "* * * * * *", taskKey)
                .IsEquivalent(new RecurringTask(taskInfo1, "1 * * * * *", taskKey))
                .Should().BeFalse("FALSE: Different Crontabs");

            new RecurringTask(taskInfo1, TimeSpan.FromMinutes(5), taskKey)
                .IsEquivalent(new RecurringTask(taskInfo1, "* * * * * *", taskKey))
                .Should().BeFalse("FALSE: Timespan & Crontab");

            Action differentIdsThrow = () => 
                new RecurringTask(taskInfo1, TimeSpan.FromMinutes(5), taskKey)
                    .IsEquivalent(new RecurringTask(taskInfo1, TimeSpan.FromMinutes(5), new TaskKey("anothertaskkey")));
            
            differentIdsThrow.ShouldThrow<Exception>();
        }

        private TaskInfo GetTestTask(Expression<Action> action) 
        {
            return action.ToTaskInfo();
        }

        private string TestMethod1(string arg)
        {
            return arg;
        }

        private void TestMethod2()
        {
            Console.WriteLine(nameof(TestMethod2));
        }
    }
}