//-----------------------------------------------------------------------------
// FILE:        JsonClient.Patch.cs
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
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;
using Neon.Collections;
using Neon.Diagnostics;
using Neon.Retry;
using Neon.Tasks;

namespace Neon.Net
{
    public partial class JsonClient : IDisposable
    {
        /// <summary>
        /// Performs an HTTP <b>PATCH</b> ensuring that a success code was returned.
        /// </summary>
        /// <param name="uri">The target URI.</param>
        /// <param name="document">
        /// The optional object to be uploaded as the request payload.  This may be JSON text, a plain
        /// old object that will be serialized as JSON or a <see cref="StreamDocument"/> to upload body
        /// data from a <see cref="Stream"/>.
        /// </param>
        /// <param name="args">The optional query arguments.</param>
        /// <param name="headers">The Optional HTTP headers.</param>
        /// <param name="cancellationToken">The optional <see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="JsonResponse"/>.</returns>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public async Task<JsonResponse> PatchAsync(
            string              uri, 
            object              document          = null, 
            ArgDictionary       args              = null, 
            ArgDictionary       headers           = null,
            CancellationToken   cancellationToken = default)
        {
            await SyncContext.Clear;
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(uri), nameof(uri));

            return await safeRetryPolicy.InvokeAsync(
                async () =>
                {
                    var requestUri = FormatUri(uri, args);

                    try
                    {
                        var client = this.HttpClient;

                        if (client == null)
                        {
                            throw new ObjectDisposedException(nameof(JsonClient));
                        }

                        var httpResponse = await client.PatchAsync(requestUri, CreateContent(document), cancellationToken: cancellationToken, headers: headers);
                        var jsonResponse = new JsonResponse(requestUri, "PATCH", httpResponse, await httpResponse.Content.ReadAsStringAsync());

                        jsonResponse.EnsureSuccess();

                        return jsonResponse;
                    }
                    catch (HttpRequestException e)
                    {
                        throw new HttpException(e, "PATCH", requestUri);
                    }
                });
        }

        /// <summary>
        /// Performs an HTTP <b>PATCH</b> returning a specific type and ensuring that a success code was returned.
        /// </summary>
        /// <typeparam name="TResult">The desired result type.</typeparam>
        /// <param name="uri">The target URI.</param>
        /// <param name="document">
        /// The optional object to be uploaded as the request payload.  This may be JSON text, a plain
        /// old object that will be serialized as JSON or a <see cref="StreamDocument"/> to upload body
        /// data from a <see cref="Stream"/>.
        /// </param>
        /// <param name="args">The optional query arguments.</param>
        /// <param name="headers">The Optional HTTP headers.</param>
        /// <param name="cancellationToken">The optional <see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="JsonResponse"/>.</returns>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public async Task<TResult> PatchAsync<TResult>(
            string              uri, 
            object              document          = null, 
            ArgDictionary       args              = null, 
            ArgDictionary       headers           = null,
            CancellationToken   cancellationToken = default)
        {
            await SyncContext.Clear;
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(uri), nameof(uri));

            var result = await safeRetryPolicy.InvokeAsync(
                async () =>
                {
                    var requestUri = FormatUri(uri, args);

                    try
                    {
                        var client = this.HttpClient;

                        if (client == null)
                        {
                            throw new ObjectDisposedException(nameof(JsonClient));
                        }

                        var httpResponse = await client.PatchAsync(requestUri, CreateContent(document), cancellationToken: cancellationToken, headers: headers);
                        var jsonResponse = new JsonResponse(requestUri, "PATCH", httpResponse, await httpResponse.Content.ReadAsStringAsync());

                        jsonResponse.EnsureSuccess();

                        return jsonResponse;
                    }
                    catch (HttpRequestException e)
                    {
                        throw new HttpException(e, "PATCH", requestUri);
                    }
                });

            return result.As<TResult>();
        }

        /// <summary>
        /// Performs an HTTP <b>PATCH</b> using a specific <see cref="IRetryPolicy"/> and ensuring that
        /// a success code was returned.
        /// </summary>
        /// <param name="retryPolicy">The retry policy or <c>null</c> to disable retries.</param>
        /// <param name="uri">The target URI.</param>
        /// <param name="document">
        /// The optional object to be uploaded as the request payload.  This may be JSON text, a plain
        /// old object that will be serialized as JSON or a <see cref="StreamDocument"/> to upload body
        /// data from a <see cref="Stream"/>.
        /// </param>
        /// <param name="args">The optional query arguments.</param>
        /// <param name="headers">The Optional HTTP headers.</param>
        /// <param name="cancellationToken">The optional <see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="JsonResponse"/>.</returns>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        /// <exception cref="HttpException">Thrown when the server responds with an HTTP error status code.</exception>
        public async Task<JsonResponse> PatchAsync(
            IRetryPolicy        retryPolicy, 
            string              uri,
            object              document          = null, 
            ArgDictionary       args              = null, 
            ArgDictionary       headers           = null,
            CancellationToken   cancellationToken = default)
        {
            await SyncContext.Clear;
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(uri), nameof(uri));

            retryPolicy = retryPolicy ?? NoRetryPolicy.Instance;

            return await retryPolicy.InvokeAsync(
                async () =>
                {
                    var requestUri = FormatUri(uri, args);

                    try
                    {
                        var client = this.HttpClient;

                        if (client == null)
                        {
                            throw new ObjectDisposedException(nameof(JsonClient));
                        }

                        var httpResponse = await client.PatchAsync(requestUri, CreateContent(document), cancellationToken: cancellationToken, headers: headers);
                        var jsonResponse = new JsonResponse(requestUri, "PATCH", httpResponse, await httpResponse.Content.ReadAsStringAsync());

                        jsonResponse.EnsureSuccess();

                        return jsonResponse;
                    }
                    catch (HttpRequestException e)
                    {
                        throw new HttpException(e, "PATCH", requestUri);
                    }
                });
        }

        /// <summary>
        /// Performs an HTTP <b>PATCH</b> without ensuring that a success code was returned.
        /// </summary>
        /// <param name="uri">The target URI.</param>
        /// <param name="document">
        /// The optional object to be uploaded as the request payload.  This may be JSON text, a plain
        /// old object that will be serialized as JSON or a <see cref="StreamDocument"/> to upload body
        /// data from a <see cref="Stream"/>.
        /// </param>
        /// <param name="args">The optional query arguments.</param>
        /// <param name="headers">The Optional HTTP headers.</param>
        /// <param name="cancellationToken">The optional <see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="JsonResponse"/>.</returns>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        public async Task<JsonResponse> PatchUnsafeAsync(
            string              uri,
            object              document          = null, 
            ArgDictionary       args              = null, 
            ArgDictionary       headers           = null,
            CancellationToken   cancellationToken = default)
        {
            await SyncContext.Clear;
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(uri), nameof(uri));

            return await unsafeRetryPolicy.InvokeAsync(
                async () =>
                {
                    var requestUri = FormatUri(uri, args);

                    try
                    {
                        var client = this.HttpClient;

                        if (client == null)
                        {
                            throw new ObjectDisposedException(nameof(JsonClient));
                        }

                        var httpResponse = await client.PatchAsync(requestUri, CreateContent(document), cancellationToken: cancellationToken, headers: headers);

                        return new JsonResponse(requestUri, "PATCH", httpResponse, await httpResponse.Content.ReadAsStringAsync());
                    }
                    catch (HttpRequestException e)
                    {
                        throw new HttpException(e, "PATCH", requestUri);
                    }
                });
        }

        /// <summary>
        /// Performs an HTTP <b>PATCH</b> using a specific <see cref="IRetryPolicy"/> and without ensuring
        /// that a success code was returned.
        /// </summary>
        /// <param name="retryPolicy">The retry policy or <c>null</c> to disable retries.</param>
        /// <param name="uri">The target URI.</param>
        /// <param name="document">
        /// The optional object to be uploaded as the request payload.  This may be JSON text, a plain
        /// old object that will be serialized as JSON or a <see cref="StreamDocument"/> to upload body
        /// data from a <see cref="Stream"/>.
        /// </param>
        /// <param name="args">The optional query arguments.</param>
        /// <param name="headers">The Optional HTTP headers.</param>
        /// <param name="cancellationToken">The optional <see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="JsonResponse"/>.</returns>
        /// <exception cref="SocketException">Thrown for network connectivity issues.</exception>
        public async Task<JsonResponse> PatchUnsafeAsync(
            IRetryPolicy        retryPolicy, 
            string              uri, 
            object              document          = null,
            ArgDictionary       args              = null, 
            ArgDictionary       headers           = null,
            CancellationToken   cancellationToken = default)
        {
            await SyncContext.Clear;
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(uri), nameof(uri));

            retryPolicy = retryPolicy ?? NoRetryPolicy.Instance;

            return await retryPolicy.InvokeAsync(
                async () =>
                {
                    var requestUri = FormatUri(uri, args);

                    try
                    {
                        var client = this.HttpClient;

                        if (client == null)
                        {
                            throw new ObjectDisposedException(nameof(JsonClient));
                        }

                        var httpResponse = await client.PatchAsync(requestUri, CreateContent(document), cancellationToken: cancellationToken, headers: headers);

                        return new JsonResponse(requestUri, "PATCH", httpResponse, await httpResponse.Content.ReadAsStringAsync());
                    }
                    catch (HttpRequestException e)
                    {
                        throw new HttpException(e, "PATCH", requestUri);
                    }
                });
        }
    }
}
