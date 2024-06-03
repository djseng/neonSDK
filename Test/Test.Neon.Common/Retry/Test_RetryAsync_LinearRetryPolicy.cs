//-----------------------------------------------------------------------------
// FILE:        Test_RetryAsync_LinearRetryPolicy.cs
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
    public class Test_RetryAsync_LinearRetryPolicy
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
        private void VerifyIntervals(List<DateTime> times, LinearRetryPolicy policy)
        {
            for (int i = 0; i < times.Count - 1; i++)
            {
                Assert.True(VerifyInterval(times[i], times[i + 1], policy.RetryInterval));
            }
        }

        [Fact]
        public void Defaults()
        {
            var policy = new LinearRetryPolicy(TransientDetector);

            Assert.Equal(RetryPolicy.DefaultMaxAttempts, policy.MaxAttempts);
            Assert.Null(policy.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(1), policy.RetryInterval);
        }

        [Fact]
        public async Task FailAll()
        {
            var policy = new LinearRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            await Assert.ThrowsAsync<TransientException>(
                async () =>
                {
                    await policy.InvokeAsync(
                        async () =>
                        {
                            times.Add(DateTime.UtcNow);
                            await Task.CompletedTask;
                            throw new TransientException();
                        });
                });

            Assert.Equal(policy.MaxAttempts , times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public async Task FailAll_Result()
        {
            var policy = new LinearRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            await Assert.ThrowsAsync<TransientException>(
                async () =>
                {
                    await policy.InvokeAsync<string>(
                        async () =>
                        {
                            times.Add(DateTime.UtcNow);
                            await Task.CompletedTask;
                            throw new TransientException();
                        });
                });

            Assert.Equal(policy.MaxAttempts, times.Count);
            VerifyIntervals(times, policy);
        }

        [Fact]
        public async Task FailImmediate()
        {
            var policy = new LinearRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            await Assert.ThrowsAsync<NotImplementedException>(
                async () =>
                {
                    await policy.InvokeAsync(
                        async () =>
                        {
                            times.Add(DateTime.UtcNow);
                            await Task.CompletedTask;
                            throw new NotImplementedException();
                        });
                });

            Assert.Single(times);
        }

        [Fact]
        public async Task FailImmediate_Result()
        {
            var policy = new LinearRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            await Assert.ThrowsAsync<NotImplementedException>(
                async () =>
                {
                    await policy.InvokeAsync<string>(
                        async () =>
                        {
                            times.Add(DateTime.UtcNow);
                            await Task.CompletedTask;
                            throw new NotImplementedException();
                        });
                });

            Assert.Single(times);
        }

        [Fact]
        public async Task FailDelayed()
        {
            var policy = new LinearRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            await Assert.ThrowsAsync<NotImplementedException>(
                async () =>
                {
                    await policy.InvokeAsync(
                        async () =>
                        {
                            times.Add(DateTime.UtcNow);
                            await Task.CompletedTask;

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
        public async Task FailDelayed_Result()
        {
            var policy = new LinearRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            await Assert.ThrowsAsync<NotImplementedException>(
                async () =>
                {
                    await policy.InvokeAsync<string>(
                        async () =>
                        {
                            times.Add(DateTime.UtcNow);
                            await Task.CompletedTask;

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
        public async Task SuccessImmediate()
        {
            var policy  = new LinearRetryPolicy(TransientDetector);
            var times   = new List<DateTime>();
            var success = false;

            await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

                    success = true;
                });

            Assert.Single(times);
            Assert.True(success);
        }

        [Fact]
        public async Task SuccessImmediate_Result()
        {
            var policy = new LinearRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            var success = await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

                    return "WOOHOO!";
                });

            Assert.Single(times);
            Assert.Equal("WOOHOO!", success);
        }

        [Fact]
        public async Task SuccessDelayed()
        {
            var policy  = new LinearRetryPolicy(TransientDetector);
            var times   = new List<DateTime>();
            var success = false;

            await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

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
        public async Task SuccessDelayed_Result()
        {
            var policy = new LinearRetryPolicy(TransientDetector);
            var times  = new List<DateTime>();

            var success = await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

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
        public async Task SuccessDelayedByType()
        {
            var policy  = new LinearRetryPolicy(typeof(NotReadyException));
            var times   = new List<DateTime>();
            var success = false;

            await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

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
        public async Task SuccessDelayedAggregateSingle()
        {
            var policy  = new LinearRetryPolicy(typeof(NotReadyException));
            var times   = new List<DateTime>();
            var success = false;

            await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

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
        public async Task SuccessDelayedAggregateArray()
        {
            var policy  = new LinearRetryPolicy(new Type[] { typeof(NotReadyException), typeof(KeyNotFoundException) });
            var times   = new List<DateTime>();
            var success = false;

            await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

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
        public async Task SuccessCustom()
        {
            var policy  = new LinearRetryPolicy(TransientDetector, maxAttempts: 4, retryInterval: TimeSpan.FromSeconds(2));
            var times   = new List<DateTime>();
            var success = false;

            Assert.Equal(4, policy.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(2), policy.RetryInterval);

            await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

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
        public async Task SuccessCustom_Result()
        {
            var policy = new LinearRetryPolicy(TransientDetector, maxAttempts: 4, retryInterval: TimeSpan.FromSeconds(2));
            var times  = new List<DateTime>();

            Assert.Equal(4, policy.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(2), policy.RetryInterval);

            var success = await policy.InvokeAsync(
                async () =>
                {
                    times.Add(DateTime.UtcNow);
                    await Task.CompletedTask;

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
        public async Task Timeout()
        {
            var policy = new LinearRetryPolicy(TransientDetector, retryInterval: TimeSpan.FromSeconds(0.5), timeout: TimeSpan.FromSeconds(2));
            var times  = new List<DateTime>();

            Assert.Equal(int.MaxValue, policy.MaxAttempts);
            Assert.Equal(TimeSpan.FromSeconds(0.5), policy.RetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(2.0), policy.Timeout);

            await Assert.ThrowsAsync<TransientException>(
                async () =>
                {
                    await policy.InvokeAsync(
                        async () =>
                        {
                            times.Add(DateTime.UtcNow);
                            await Task.CompletedTask;

                            throw new TransientException();
                        });
                });

            Assert.True(times.Count >= 4);

            // We'll wait a bit longer to enure that any (incorrect) deadline computed
            // by the policy when constructed above does not impact a subsequent run.

            await Task.Delay(TimeSpan.FromSeconds(4));

            times.Clear();

            Assert.Equal(TimeSpan.FromSeconds(0.5), policy.RetryInterval);
            Assert.Equal(TimeSpan.FromSeconds(2), policy.Timeout);

            await Assert.ThrowsAsync<TransientException>(
                async () =>
                {
                    await policy.InvokeAsync(
                        async () =>
                        {
                            times.Add(DateTime.UtcNow);
                            await Task.CompletedTask;

                            throw new TransientException();
                        });
                });

            Assert.True(4 <= times.Count && times.Count <= 6);
        }

        [Fact]
        public async Task Cancel()
        {
            // Use a cancellation token to cancel an operation.

            var cts    = new CancellationTokenSource();
            var policy = new LinearRetryPolicy(typeof(TransientException), maxAttempts: 6, retryInterval: TimeSpan.FromSeconds(1));

            cts.CancelAfter(TimeSpan.FromSeconds(2));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                {
                    await policy.InvokeAsync(
                        async () =>
                        {
                            await Task.CompletedTask;
                            throw new TransientException();
                        },
                        cts.Token);
                });
        }
    }
}
