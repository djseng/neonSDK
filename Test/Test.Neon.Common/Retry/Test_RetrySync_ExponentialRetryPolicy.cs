//-----------------------------------------------------------------------------
// FILE:        Test_RetrySync_ExponentialRetryPolicy.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright © 2005-2024 by NEONFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Retry;
using Neon.Xunit;

using Xunit;

namespace TestCommon
{
    [Trait(TestTrait.Category, TestArea.NeonCommon)]
    public class Test_RetrySync_ExponentialRetryPolicy
    {
        private class TransientException : Exception
        {
        }

        private bool TransientDetector(Exception e)
        {
            return e is TransientException;
        }

        private bool VerifyInterval(DateTime time0, DateTime time1, TimeSpan minInterval)
        {
            // Verify that [time1] is greater than [time0] by at least [minInterval]
            // allowing 100ms of slop due to the fact that Task.Delay() sometimes 
            // delays for less than the requested timespan.

            return time1 - time0 > minInterval - TimeSpan.FromMilliseconds(100);
        }

        /// <summary>
        /// Verify that operation retry times are consistent with the retry policy.
        /// </summary>
        /// <param name="times"></param>
        /// <param name="policy"></param>
        private void VerifyIntervals(List<DateTime> times, ExponentialRetryPolicy policy)
        {
            var interval = policy.InitialRetryInterval;

            for (int i = 0; i < times.Count - 1; i++)
            {
                Assert.True(VerifyInterval(times[i], times[i + 1], interval));

                interval = TimeSpan.FromTicks(interval.Ticks * 2);

                if (interval > policy.MaxRetryInterval)
                {
                    interval = policy.MaxRetryInterval;
                }
            }
        }

        [Fact]
        public void Defaults()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector, categoryName: "test");

            Assert.Equal(RetryPolicy.DefaultMaxAttempts, policy.MaxAttempts);
            Assert.Null(policy.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(1), policy.InitialRetryInterval);
            Assert.Equal(TimeSpan.FromHours(24), policy.MaxRetryInterval);
        }

        [Fact]
        public void FailAll()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            Assert.Throws<TransientException>(
                () =>
                {
                    policy.Invoke(
                        () =>
                        {
                            times.Add(DateTime.UtcNow);
                            throw new TransientException();
                        });
                });

            Assert.Equal(policy.MaxAttempts , times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void FailAll_Result()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            Assert.Throws<TransientException>(
                () =>
                {
                    policy.Invoke<string>(
                        () =>
                        {
                            times.Add(DateTime.UtcNow);
                            throw new TransientException();
                        });
                });

            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void FailImmediate()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            Assert.Throws<NotImplementedException>(
                () =>
                {
                    policy.Invoke(
                        () =>
                        {
                            times.Add(DateTime.UtcNow);
                            throw new NotImplementedException();
                        });
                });

            Assert.Single(times);
        }

        [Fact]
        public void FailImmediate_Result()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            Assert.Throws<NotImplementedException>(
                () =>
                {
                    policy.Invoke<string>(
                        () =>
                        {
                            times.Add(DateTime.UtcNow);
                            throw new NotImplementedException();
                        });
                });

            Assert.Single(times);
        }

        [Fact]
        public void FailDelayed()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            Assert.Throws<NotImplementedException>(
                () =>
                {
                    policy.Invoke(
                        () =>
                        {
                            times.Add(DateTime.UtcNow);

                            if (times.Count < 2)
                            {
                                throw new TransientException();
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }
                        });
                });

            Assert.Equal(2, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void FailDelayed_Result()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            Assert.Throws<NotImplementedException>(
                () =>
                {
                    policy.Invoke<string>(
                        () =>
                        {
                            times.Add(DateTime.UtcNow);

                            if (times.Count < 2)
                            {
                                throw new TransientException();
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }
                        });
                });

            Assert.Equal(2, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void SuccessImmediate()
        {
            var policy  = new ExponentialRetryPolicy(TransientDetector);
            var times   = new List<DateTime>();
            var success = false;

            policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    success = true;
                });

            Assert.Single(times);
            Assert.True(success);
        }

        [Fact]
        public void SuccessImmediate_Result()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector);
            var times   = new List<DateTime>();

            var success = policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    return "WOOHOO!";
                });

            Assert.Single(times);
            Assert.Equal("WOOHOO!", success);
        }

        [Fact]
        public void SuccessDelayed()
        {
            var policy  = new ExponentialRetryPolicy(TransientDetector);
            var times   = new List<DateTime>();
            var success = false;

            policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    if (times.Count < policy.MaxAttempts)
                    {
                        throw new TransientException();
                    }

                    success = true;
                });

            Assert.True(success);
            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void SuccessDelayed_Result()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            var success = policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    if (times.Count < policy.MaxAttempts)
                    {
                        throw new TransientException();
                    }

                    return "WOOHOO!";
                });

            Assert.Equal("WOOHOO!", success);
            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void SuccessDelayedByType()
        {
            var policy  = new ExponentialRetryPolicy(typeof(NotReadyException));
            var times   = new List<DateTime>();
            var success = false;

            policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    if (times.Count < policy.MaxAttempts)
                    {
                        throw new NotReadyException();
                    }

                    success = true;
                });

            Assert.True(success);
            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void SuccessDelayedAggregateSingle()
        {
            var policy  = new ExponentialRetryPolicy(typeof(NotReadyException));
            var times   = new List<DateTime>();
            var success = false;

            policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    if (times.Count < policy.MaxAttempts)
                    {
                        throw new AggregateException(new NotReadyException());
                    }

                    success = true;
                });

            Assert.True(success);
            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void SuccessDelayedAggregateArray()
        {
            var policy  = new ExponentialRetryPolicy(new Type[] { typeof(NotReadyException), typeof(KeyNotFoundException) });
            var times   = new List<DateTime>();
            var success = false;

            policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    if (times.Count < policy.MaxAttempts)
                    {
                        if (times.Count % 1 == 0)
                        {
                            throw new AggregateException(new NotReadyException());
                        }
                        else
                        {
                            throw new AggregateException(new KeyNotFoundException());
                        }
                    }

                    success = true;
                });

            Assert.True(success);
            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void SuccessCustom()
        {
            var policy  = new ExponentialRetryPolicy(TransientDetector, maxAttempts: 6, initialRetryInterval: TimeSpan.FromSeconds(0.5), maxRetryInterval: TimeSpan.FromSeconds(4));
            var times   = new List<DateTime>();
            var success = false;

            Assert.Equal(6, policy.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(0.5), policy.InitialRetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(4), policy.MaxRetryInterval);

            policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    if (times.Count < policy.MaxAttempts)
                    {
                        throw new TransientException();
                    }

                    success = true;
                });

            Assert.True(success);
            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void SuccessCustom_Result()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector, maxAttempts: 6, initialRetryInterval: TimeSpan.FromSeconds(0.5), maxRetryInterval: TimeSpan.FromSeconds(4));
            var times  = new List<DateTime>();

            Assert.Equal(6, policy.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(0.5), policy.InitialRetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(4), policy.MaxRetryInterval);

            var success = policy.Invoke(
                () =>
                {
                    times.Add(DateTime.UtcNow);

                    if (times.Count < policy.MaxAttempts)
                    {
                        throw new TransientException();
                    }

                    return "WOOHOO!";
                });

            Assert.Equal("WOOHOO!", success);
            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public void Timeout()
        {
            var policy = new ExponentialRetryPolicy(TransientDetector, initialRetryInterval: TimeSpan.FromSeconds(0.5), maxRetryInterval: TimeSpan.FromSeconds(4), timeout: TimeSpan.FromSeconds(1.5));
            var times  = new List<DateTime>();

            Assert.Equal(int.MaxValue, policy.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(0.5), policy.InitialRetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(4), policy.MaxRetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(1.5), policy.Timeout);

            Assert.Throws<TransientException>(
                () =>
                {
                    policy.Invoke(
                        () =>
                        {
                            times.Add(DateTime.UtcNow);

                            throw new TransientException();
                        });
                });

            Assert.Equal(3, times.Count);

            // We'll wait a bit longer to enure that any (incorrect) deadline computed
            // by the policy when constructed above does not impact a subsequent run.

            Thread.Sleep(TimeSpan.FromSeconds(4));

            times.Clear();

            Assert.Equal(TimeSpan.FromSeconds(0.5), policy.InitialRetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(4), policy.MaxRetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(1.5), policy.Timeout);

            Assert.Throws<TransientException>(
                () =>
                {
                    policy.Invoke(
                        () =>
                        {
                            times.Add(DateTime.UtcNow);

                            throw new TransientException();
                        });
                });

            Assert.Equal(3, times.Count);
        }

        [Fact]
        public void Cancel()
        {
            // Have test code throw TimeoutExceptions which will be considered to be transient and
            // then in another task, have the cancellation token cancel itself and then verify that
            // the policy invoke fails with a CancellationException.

            var cts    = new CancellationTokenSource();
            var policy = new ExponentialRetryPolicy(typeof(TransientException), maxAttempts: 6, initialRetryInterval: TimeSpan.FromSeconds(1), maxRetryInterval: TimeSpan.FromSeconds(1));

            cts.CancelAfter(TimeSpan.FromSeconds(2));

            Assert.ThrowsAny<OperationCanceledException>(
                () =>
                {
                    policy.Invoke(
                        () =>
                        {
                            throw new TransientException();
                        },
                        cts.Token);
                });
        }
    }
}
