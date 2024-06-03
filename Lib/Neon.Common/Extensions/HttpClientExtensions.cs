//-----------------------------------------------------------------------------
// FILE:        HttpClientExtensions.cs
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
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Diagnostics;
using Neon.Collections;
using Neon.Tasks;
using System.Runtime.CompilerServices;

namespace Neon.Common
{
    /// <summary>
    /// <see cref="HttpClient"/> extension methods, mostly related to easily supporting custom headers.
    /// </summary>
    public static partial class HttpClientExtensions
    {
        private static HttpMethod deleteMethod  = new HttpMethod("DELETE");
        private static HttpMethod headMethod    = new HttpMethod("HEAD");
        private static HttpMethod optionsMethod = new HttpMethod("OPTIONS");
        private static HttpMethod patchMethod   = new HttpMethod("PATCH");

        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Adds headers to an HTTP request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="headers">Optionally specifies a dictionary of headers.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddHeaders(HttpRequestMessage request, ArgDictionary headers = null)
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value.ToString());
                }
            }
        }

        /// <summary>
        /// Sends a GET request to the specified string URI.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> GetAsync(
            this HttpClient         client, 
            Uri                     requestUri, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a GET request to a specified <see cref="Uri"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> GetAsync(
            this HttpClient         client, 
            string                  requestUri, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a GET to a specified string URI and returns the response body as a byte array.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <returns>The response byte array.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<byte[]> GetByteArrayAsync(
            this HttpClient         client, 
            string                  requestUri, 
            ArgDictionary           headers = null)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            AddHeaders(request, headers);

            var response = await client.SendAsync(request);

            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Sends a GET to a specified <see cref="Uri"/> and returns the response body as a byte array.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <returns>The response byte array.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<byte[]> GetByteArrayAsync(
            this HttpClient         client,
            Uri                     requestUri,
            ArgDictionary           headers = null)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            AddHeaders(request, headers);

            var response = await client.SendAsync(request);

            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Sends a GET to a specified string URI and returns the response body as a <see cref="Stream"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <returns>The response stream.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<Stream> GetStreamAsync(
            this HttpClient         client, 
            string                  requestUri, 
            ArgDictionary           headers = null)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            AddHeaders(request, headers);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            return await response.Content.ReadAsStreamAsync();
        }

        /// <summary>
        /// Sends a GET to a specified <see cref="Uri"/> and returns the response body as a <see cref="Stream"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <returns>The response stream.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<Stream> GetStreamAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            ArgDictionary           headers = null)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            AddHeaders(request, headers);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            return await response.Content.ReadAsStreamAsync();
        }

        /// <summary>
        /// Sends a GET request to a string URI and returns the response as a string.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <returns>The response string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<string> GetStringAsync(
            this HttpClient         client, 
            string                  requestUri,
            ArgDictionary           headers = null)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            AddHeaders(request, headers);

            var response = await client.SendAsync(request);

            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Sends a GET request to a <see cref="Uri"/> and returns the response as a string.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <returns>The response string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<string> GetStringAsync(
            this HttpClient         client,
            Uri                     requestUri,
            ArgDictionary           headers = null)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

            AddHeaders(request, headers);

            var response = await client.SendAsync(request);

            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Sends a POST request to a string URI.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> PostAsync(
            this HttpClient         client, 
            string                  requestUri,
            HttpContent             content,
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);

            request.Content = content;

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a POST request to a <see cref="Uri"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> PostAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);

            request.Content = content;

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a PUT request to a string URI.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> PutAsync(
            this HttpClient         client,
            string                  requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Put, requestUri);

            request.Content = content;

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a PUT request to a <see cref="Uri"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> PutAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(HttpMethod.Put, requestUri);

            request.Content = content;

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }


        /// <summary>
        /// Sends a DELETE request to a string URI.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> DeleteAsync(
            this HttpClient         client, 
            string                  requestUri, 
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(deleteMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a DELETE request to a <see cref="Uri"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> DeleteAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(deleteMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a PATCH request to a string URI.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> PatchAsync(
            this HttpClient         client, 
            string                  requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(patchMethod, requestUri);

            request.Content = content;

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a PATCH request to a <see cref="Uri"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> PatchAsync(
            this HttpClient         client,
            Uri                     requestUri, 
            HttpContent             content, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(patchMethod, requestUri);

            request.Content = content;

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a OPTIONS request to a string URI.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> OptionsAsync(
            this HttpClient         client,
            string                  requestUri,
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(optionsMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a OPTIONS request to a <see cref="Uri"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> OptionsAsync(
            this HttpClient         client,
            Uri                     requestUri,
            HttpContent             content           = null,
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(optionsMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a HEAD request to a string URI.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> HeadAsync(
            this HttpClient         client,
            string                  requestUri, 
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(headMethod, requestUri);

            if (content == null)
            {
                request.Content = content;
            }

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends a HEAD request to a <see cref="Uri"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="content">The content to be sent to the server.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        public static async Task<HttpResponseMessage> HeadAsync(
            this HttpClient         client, 
            Uri                     requestUri, 
            HttpContent             content           = null, 
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default, 
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            var request = new HttpRequestMessage(headMethod, requestUri);

            if (content != null)
            {
                request.Content = content;
            }

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }

        /// <summary>
        /// Sends an <see cref="HttpRequestMessage"/>.
        /// </summary>
        /// <param name="client">The client.</param>
        /// <param name="request">The request.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="completionOption">
        /// Optionally specifies when the operation should complete (as soon as a response is available or after
        /// reading the whole response content).
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the request has already been sent by the <see cref="HttpClient"/> class.</exception>
        public static async Task<HttpResponseMessage> SendAsync(
            this HttpClient         client,
            HttpRequestMessage      request,
            ArgDictionary           headers           = null,
            HttpCompletionOption    completionOption  = default,
            CancellationToken       cancellationToken = default)
        {
            await SyncContext.Clear;

            AddHeaders(request, headers);

            return await client.SendAsync(request, completionOption, cancellationToken);
        }
    }
}
