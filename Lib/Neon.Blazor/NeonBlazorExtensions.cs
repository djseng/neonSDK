//-----------------------------------------------------------------------------
// FILE:        NeonBlazorExtensions.cs
// CONTRIBUTOR: Marcus Bowyer
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

using Microsoft.Extensions.DependencyInjection;

namespace Neon.Blazor
{
    public static class NeonBlazorExtensions
    {
        public static IServiceCollection AddNeonBlazor(
            this IServiceCollection builder)
        {
            builder
                .AddScoped<BodyOutlet>()
                .AddScoped<MobileDetector>()
                .AddScoped<FileDownloader>();

            return builder;
        }
    }
}
