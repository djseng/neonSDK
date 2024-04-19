//-----------------------------------------------------------------------------
// FILE:        DeploymentHelper.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright © 2005-2023 by NEONFORGE LLC.  All rights reserved.
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;
using Neon.Cryptography;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.Tasks;

namespace Neon.Deployment
{
    /// <summary>
    /// Deployment related defintions and utilities.
    /// </summary>
    public static class DeploymentHelper
    {
        /// <summary>
        /// Identifies the named pipe used to communicate with the Neon profile
        /// service running on the local workstation to query for user profile
        /// information as well as secrets.
        /// </summary>
        public const string NeonProfileServicePipe = "neon-profile-service";

        /// <summary>
        /// The HTTP Content-Type used for multi-part download manifest.
        /// </summary>
        public const string DownloadManifestContentType = "application/vnd+neonforge.download+manifest+json";

        /// <summary>
        /// Clears the Powershell command history.  It's possible that scripts and
        /// GitHub workflow runs may leave sensitive information in the command
        /// history which could become a security vunerability.
        /// </summary>
        public static void ClearPowershellHistory()
        {
            // Inspired by: https://www.shellhacks.com/clear-history-powershell/

            using (var tempFile = new TempFile(suffix: ".ps1"))
            {
                File.WriteAllText(tempFile.Path, "Remove-Item (Get-PSReadlineOption).HistorySavePath");
                
                var exitCode = NeonHelper.ExecuteShell($"pwsh -f \"{tempFile.Path}\"");

                if (exitCode != 0)
                {
                    throw new ExecuteException(exitCode, "Command failed.");
                }
            }
        }

        /// <summary>
        /// Synchronously downloads and assembles a multi-part file as specified by a <see cref="Neon.Deployment.DownloadManifest"/>.
        /// </summary>
        /// <param name="download">The download details.</param>
        /// <param name="targetPath">The target file path.</param>
        /// <param name="progressAction">Optionally specifies an action to be called with the the percentage downloaded.</param>
        /// <param name="retry">Optionally specifies the retry policy.  This defaults to a reasonable policy.</param>
        /// <param name="partTimeout">Optionally specifies the HTTP download timeout for each part (defaults to 10 minutes).</param>
        /// <param name="strictCheck">
        /// <para>
        /// Optionally used to enable a slow but more comprehensive check of any existing file.
        /// When this is enabled and the download file already exists along with its MD5 hash file,
        /// the method will assume that the existing file matches when the file size is the same
        /// as specified in the manifest and manifest overall MD5 matches the local MD5 file.
        /// </para>
        /// <para>
        /// Otherwise, this method will need to compute the MD5 hashes for the existing file parts
        /// and compare those to the part MD5 hashes in the manifest, which can take quite a while
        /// for large files.
        /// </para>
        /// <para>
        /// This defaults to <c>false</c>.
        /// </para>
        /// </param>
        /// <exception cref="IOException">Thrown when the download is corrupt.</exception>
        /// <exception cref="SocketException">Thrown for network errors.</exception>
        /// <exception cref="HttpException">Thrown for HTTP network errors.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation was cancelled.</exception>
        public static void DownloadMultiPart(
            DownloadManifest            download, 
            string                      targetPath, 
            DownloadProgressDelegate    progressAction = null, 
            IRetryPolicy                retry          = null,
            TimeSpan                    partTimeout    = default,
            bool                        strictCheck    = false)
        {
            DownloadMultiPartAsync(download, targetPath, progressAction, partTimeout, retry, strictCheck).WaitWithoutAggregate();
        }

        /// <summary>
        /// Asynchronously downloads and assembles a multi-part file as specified by a source URI.
        /// </summary>
        /// <param name="uri">The URI for the source URI holding the <see cref="DownloadManifest"/> details as JSON.</param>
        /// <param name="targetPath">The target file path.</param>
        /// <param name="progressAction">Optionally specifies an action to be called with the the percentage downloaded.</param>
        /// <param name="retry">Optionally specifies the retry policy.  This defaults to a reasonable policy.</param>
        /// <param name="partTimeout">Optionally specifies the HTTP download timeout for each part (defaults to 10 minutes).</param>
        /// <param name="strictCheck">
        /// <para>
        /// Optionally used to enable a slow but more comprehensive check of any existing file.
        /// When this is enabled and the download file already exists along with its MD5 hash file,
        /// the method will assume that the existing file matches when the file size is the same
        /// as specified in the manifest and manifest overall MD5 matches the local MD5 file.
        /// </para>
        /// <para>
        /// Otherwise, this method will need to compute the MD5 hashes for the existing file parts
        /// and compare those to the part MD5 hashes in the manifest, which can take quite a while
        /// for large files.
        /// </para>
        /// <para>
        /// This defaults to <c>false</c>.
        /// </para>
        /// </param>
        /// <exception cref="IOException">Thrown when the download is corrupt.</exception>
        /// <exception cref="SocketException">Thrown for network errors.</exception>
        /// <exception cref="HttpException">Thrown for HTTP network errors.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation was cancelled.</exception>
        /// <exception cref="FormatException">Thrown when the object retrieved from <paramref name="uri"/> doesn't have the <see cref="DeploymentHelper.DownloadManifestContentType"/> content type.</exception>
        public static void DownloadMultiPart(
            string                      uri,
            string                      targetPath,
            DownloadProgressDelegate    progressAction = null,
            IRetryPolicy                retry          = null,
            TimeSpan                    partTimeout    = default,
            bool                        strictCheck    = true)
        {
            DownloadMultiPartAsync(uri, targetPath, progressAction, partTimeout, retry, strictCheck).WaitWithoutAggregate();
        }

        /// <summary>
        /// Asynchronously downloads and assembles a multi-part file  as specified by a <see cref="Neon.Deployment.DownloadManifest"/>.
        /// </summary>
        /// <param name="manifest">The download details.</param>
        /// <param name="targetPath">The target file path.</param>
        /// <param name="progressAction">Optionally specifies an action to be called with the the percentage downloaded.</param>
        /// <param name="partTimeout">Optionally specifies the HTTP download timeout for each part (defaults to 10 minutes).</param>
        /// <param name="retry">Optionally specifies the retry policy.  This defaults to a reasonable policy.</param>
        /// <param name="strictCheck">
        /// <para>
        /// Optionally used to disable a slow but more comprehensive check of any existing file.
        /// When this is disabled and the download file already exists along with its MD5 hash file,
        /// the method will assume that the existing file matches when the file size is the same
        /// as specified in the manifest and manifest overall MD5 matches the local MD5 file.
        /// </para>
        /// <para>
        /// Otherwise when <paramref name="strictCheck"/> is <c>true</c>, this method will need to 
        /// compute the MD5 hashes for the existing file parts and compare those to the part MD5
        /// hashes in the manifest, which can take quite a while for large files.
        /// </para>
        /// <para>
        /// This defaults to <c>true</c>.
        /// </para>
        /// </param>
        /// <param name="cancellationToken">Optionally specifies the operation cancellation token.</param>
        /// <returns>The path to the downloaded file.</returns>
        /// <exception cref="IOException">Thrown when the download is corrupt.</exception>
        /// <exception cref="SocketException">Thrown for network errors.</exception>
        /// <exception cref="HttpException">Thrown for HTTP network errors.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation was cancelled.</exception>
        /// <remarks>
        /// <para>
        /// This method downloads the file specified by <paramref name="manifest"/> to the folder specified, creating 
        /// the folder first when required.  The file will be downloaded in parts, where each part will be validated
        /// by comparing the part's MD5 hash (when present) with the computed value.  The output file will be named 
        /// <see cref="DownloadManifest.Name"/> and the overall MD5 hash will also be saved using the same file name but
        /// <b>adding</b> the <b>.md5</b> extension.
        /// </para>
        /// <para>
        /// This method will continue downloading a partially downloaded file.  This works by validating the already
        /// downloaded parts against their MD5 hashes and then continuing part downloads after the last valid part.
        /// Nothing will be downloaded when the existing file is fully formed.
        /// </para>
        /// <note>
        /// The target files (output and MD5) will be deleted when download appears to be corrupt.
        /// </note>
        /// </remarks>
        public static async Task<string> DownloadMultiPartAsync(
            DownloadManifest            manifest, 
            string                      targetPath, 
            DownloadProgressDelegate    progressAction    = null, 
            TimeSpan                    partTimeout       = default, 
            IRetryPolicy                retry             = null,
            bool                        strictCheck       = false,
            CancellationToken           cancellationToken = default)
        {
            await SyncContext.Clear;
            Covenant.Requires<ArgumentNullException>(manifest != null, nameof(manifest));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(targetPath), nameof(targetPath));

            retry = retry ?? new ExponentialRetryPolicy(TransientDetector.NetworkOrHttp, maxAttempts: 5);

            if (partTimeout <= TimeSpan.Zero)
            {
                partTimeout = TimeSpan.FromMinutes(10);
            }

            var targetFolder = Path.GetDirectoryName(targetPath);

            Directory.CreateDirectory(targetFolder);

            var targetMd5Path  = Path.Combine(Path.GetDirectoryName(targetPath), Path.GetFileName(targetPath) + ".md5");
            var nextPartNumber = 0;
            var existingSize   = File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0;

            // When we're not doing strict checks, and the target file already exists
            // along with its MD5 hash file:
            //
            //      1. Compare the manifest MD5 with the local MD5 hash file and
            //         quickly continue with the download when these don't match.
            //
            //      2. When the MD5 files match and the download file's length equals
            //         the overall length specified by the manifest, we're going to
            //         assume that the rest of the file is OK, to speed things up
            //         because computing hashes on large files is slow.
            //
            //      3. Otherwise, we'll validate the file and download any missing 
            //         parts.

            if (!strictCheck)
            {
                if (File.Exists(targetPath) && File.Exists(targetMd5Path) && File.ReadAllText(targetMd5Path).Trim() == manifest.Md5 && manifest.Size == existingSize)
                {
                    return targetPath;
                }
            }

            // We'll recompute this below

            NeonHelper.DeleteFile(targetMd5Path);

            // Delete the existing file when its size is greater than the overall download
            // size specified by the manifest.  This should never happen in production and
            // is probably a sign that the existing file is corrupt.
            //
            // When the existing size is less that or equal to the manifest size, we'll verify
            // what's been downloaded so far and try to continue downloading the remaining parts
            // below.

            if (existingSize > manifest.Size)
            {
                NeonHelper.DeleteFile(targetPath);
                existingSize = 0;
            }

            // Validate the parts of any existing target file to determine where
            // to start downloading missing parts.

            if (File.Exists(targetPath))
            {
                using (var output = new FileStream(targetPath, System.IO.FileMode.Open, FileAccess.ReadWrite))
                {
                    var pos = 0L;

                    foreach (var part in manifest.Parts.OrderBy(part => part.Number))
                    {
                        progressAction?.Invoke(DownloadProgressType.Check, (int)((double)pos / (double)manifest.Size * 100.0));

                        // Handle a partially downloaded part.  We're going to truncate the file to remove
                        // the partial part and then break to start re-downloading from that part.

                        if (output.Length < pos + part.Size)
                        {
                            output.SetLength(pos);

                            nextPartNumber = part.Number;
                            break;
                        }

                        // Validate the part MD5.  We're going to truncate the file to remove the
                        // partial part and then break to start re-downloading the part.

                        using (var partStream = new SubStream(output, pos, part.Size))
                        {
                            if (CryptoHelper.ComputeMD5String(partStream) != part.Md5)
                            {
                                output.SetLength(pos);

                                nextPartNumber = part.Number;
                                break;
                            }
                        }

                        pos           += part.Size;
                        nextPartNumber = part.Number + 1;
                    }
                }
            }

            // Download any remaining parts.

            if (progressAction != null && !progressAction.Invoke(DownloadProgressType.Download, 0))
            {
                return targetPath;
            }

            if (nextPartNumber == manifest.Parts.Count)
            {
                progressAction?.Invoke(DownloadProgressType.Download, 100);
                return targetPath;
            }

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = partTimeout;

                    using (var output = new FileStream(targetPath, System.IO.FileMode.OpenOrCreate, FileAccess.ReadWrite))
                    {
                        // Determine the starting position of the next part to be downloaded.

                        var pos = manifest.Parts
                            .Where(part => part.Number < nextPartNumber)
                            .Sum(part => part.Size);

                        // Download the remaining parts.

                        foreach (var part in manifest.Parts
                            .Where(part => part.Number >= nextPartNumber)
                            .OrderBy(part => part.Number))
                        {
                            await retry.InvokeAsync(
                                async () =>
                                {
                                    output.Position = pos;

                                    var response = await httpClient.GetAsync(part.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                                    response.EnsureSuccessStatusCodeEx();

                                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                                    {
                                        await contentStream.CopyToAsync(output, cancellationToken);
                                    }
                                });

                            // Ensure that the downloaded part size matches the specification.

                            if (output.Position - pos != part.Size)
                            {
                                throw new IOException($"[{manifest.Name}]: Part [{part.Number}] actual size [{output.Position - pos}] does not match the expected size [{part.Size}].");
                            }

                            // Ensure that the downloaded part MD5 matches the specification.

                            using (var subStream = new SubStream(output, pos, part.Size))
                            {
                                var actualMd5 = CryptoHelper.ComputeMD5String(subStream);

                                if (actualMd5 != part.Md5)
                                {
                                    throw new IOException($"[{manifest.Name}]: Part [{part.Number}] actual MD5 [{actualMd5}] does not match the expected MD5 [{part.Md5}].");
                                }
                            }

                            pos += part.Size;

                            if (progressAction != null && !progressAction.Invoke(DownloadProgressType.Download, (int)(100.0 * ((double)part.Number / (double)manifest.Parts.Count))))
                            {
                                return targetPath;
                            }
                        }

                        if (output.Length != manifest.Size)
                        {
                            throw new IOException($"[{manifest.Name}]: Expected size [{manifest.Size}] got [{output.Length}].");
                        }
                    }

                    progressAction?.Invoke(DownloadProgressType.Download, 100);
                    File.WriteAllText(targetMd5Path, manifest.Md5, Encoding.ASCII);

                    return targetPath;
                }
            }
            catch (IOException)
            {
                NeonHelper.DeleteFile(targetPath);
                NeonHelper.DeleteFile(targetMd5Path);

                throw;
            }
        }

        /// <summary>
        /// Asynchronously downloads and assembles a multi-part file  as specified by a <see cref="Neon.Deployment.DownloadManifest"/>.
        /// </summary>
        /// <param name="uri">The URI for the source URI holding the <see cref="DownloadManifest"/> details as JSON.</param>
        /// <param name="targetPath">The target file path.</param>
        /// <param name="progressAction">Optionally specifies an action to be called with the the percentage downloaded.</param>
        /// <param name="partTimeout">Optionally specifies the HTTP download timeout for each part (defaults to 10 minutes).</param>
        /// <param name="retry">Optionally specifies the retry policy.  This defaults to a reasonable policy.</param>
        /// <param name="strictCheck">
        /// <para>
        /// Optionally used to enable a slow but more comprehensive check of any existing file.
        /// When this is enabled and the download file already exists along with its MD5 hash file,
        /// the method will assume that the existing file matches when the file size is the same
        /// as specified in the manifest and manifest overall MD5 matches the local MD5 file.
        /// </para>
        /// <para>
        /// Otherwise, this method will need to compute the MD5 hashes for the existing file parts
        /// and compare those to the part MD5 hashes in the manifest, which can take quite a while
        /// for large files.
        /// </para>
        /// <para>
        /// This defaults to <c>false</c>.
        /// </para>
        /// </param>
        /// <param name="cancellationToken">Optionally specifies the operation cancellation token.</param>
        /// <returns>The path to the downloaded file.</returns>
        /// <exception cref="IOException">Thrown when the download is corrupt.</exception>
        /// <exception cref="SocketException">Thrown for network errors.</exception>
        /// <exception cref="HttpException">Thrown for HTTP network errors.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation was cancelled.</exception>
        /// <exception cref="FormatException">Thrown when the object retrieved from <paramref name="uri"/> doesn't have the <see cref="DeploymentHelper.DownloadManifestContentType"/> content type.</exception>
        /// <remarks>
        /// <para>
        /// This method downloads the file specified by <paramref name="uri"/> to the folder specified, creating 
        /// the folder first when required.  The file will be downloaded in parts, where each part will be validated
        /// by comparing the part's MD5 hash (when present) with the computed value.  The output file will be named 
        /// <see cref="DownloadManifest.Name"/> and the overall MD5 hash will also be saved using the same file name but
        /// <b>adding</b> the <b>.md5</b> extension.
        /// </para>
        /// <para>
        /// This method will continue downloading a partially downloaded file.  This works by validating the already
        /// downloaded parts against their MD5 hashes and then continuing part downloads after the last valid part.
        /// Nothing will be downloaded when the existing file is fully formed.
        /// </para>
        /// <note>
        /// The target files (output and MD5) will be deleted when download appears to be corrupt.
        /// </note>
        /// </remarks>
        public static async Task<string> DownloadMultiPartAsync(
            string                      uri, 
            string                      targetPath, 
            DownloadProgressDelegate    progressAction    = null, 
            TimeSpan                    partTimeout       = default, 
            IRetryPolicy                retry             = null,
            bool                        strictCheck       = false,
            CancellationToken           cancellationToken = default)
        {
            await SyncContext.Clear;

            DownloadManifest manifest;

            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetSafeAsync(uri);

                // $hack(jefflill):
                //
                // We're having trouble setting the [Content-Type] header when publishing
                // node images to S3.  I'm going to disable this check since it's not really
                // that important since deserialization will fail if the file is bad.
                //
                //      https://github.com/nforgeio/neonCLOUD/issues/421

                //if (!response.Content.Headers.ContentType.MediaType.Equals(DeploymentHelper.DownloadManifestContentType))
                //{
                //    const string s3CustonContentType = "x-amz-meta-content-type";

                //    if (!response.Content.Headers.Contains(s3CustonContentType) ||
                //        response.Content.Headers.GetValues(s3CustonContentType).FirstOrDefault() != DeploymentHelper.DownloadManifestContentType)
                //    {
                //        throw new FormatException($"The content type for [{uri}] is [{response.Content.Headers.ContentType.MediaType}].  [{DeploymentHelper.DownloadManifestContentType}] was expected.");
                //    }
                //}

                manifest = NeonHelper.JsonDeserialize<DownloadManifest>(await response.Content.ReadAsStringAsync());
            }

            return await DownloadMultiPartAsync(manifest, targetPath, progressAction, partTimeout, retry, strictCheck, cancellationToken);
        }
    }
}
