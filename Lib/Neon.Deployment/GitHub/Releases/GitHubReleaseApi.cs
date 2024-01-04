//-----------------------------------------------------------------------------
// FILE:        GitHubReleaseApi.cs
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Neon.Common;
using Neon.Cryptography;
using Neon.IO;
using Neon.Net;
using Neon.Retry;

using Octokit;

namespace Neon.Deployment
{
    /// <summary>
    /// Used to publish and manage GitHub releases.
    /// </summary>
    /// <remarks>
    /// <note>
    /// This API doesn't currently support modifying assets of
    /// of published releases although GitHub does support this.
    /// We may add this functionality in the future.
    /// </note>
    /// </remarks>
    public class GitHubReleaseApi
    {
        /// <summary>
        /// Internal constructor.
        /// </summary>
        internal GitHubReleaseApi()
        {
        }

        /// <summary>
        /// Creates a GitHub release.
        /// </summary>
        /// <param name="repo">Identifies the target repo.</param>
        /// <param name="tagName">Specifies the tag to be referenced by the release.</param>
        /// <param name="releaseName">Optionally specifies the release name (defaults to <paramref name="tagName"/>).</param>
        /// <param name="body">Optionally specifies the markdown formatted release notes.</param>
        /// <param name="draft">Optionally indicates that the release won't be published immediately.</param>
        /// <param name="prerelease">Optionally indicates that the release is not production ready.</param>
        /// <param name="branch">Optionally identifies the branch to be tagged.  This defaults to <b>master</b> or <b>main</b> when either of those branches are already present.</param>
        /// <returns>The newly created <see cref="Release"/>.</returns>
        /// <remarks>
        /// <para>
        /// If the <paramref name="tagName"/> doesn't already exist in the repo, this method will
        /// tag the latest commit on the specified <paramref name="branch"/> or else the defailt branch
        /// in the target repo and before creating the release.
        /// </para>
        /// </remarks>
        public Release Create(string repo, string tagName, string releaseName = null, string body = null, bool draft = false, bool prerelease = false, string branch = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(tagName), nameof(tagName));

            releaseName = releaseName ?? tagName;

            var repoPath = GitHubRepoPath.Parse(repo);
            var client   = GitHub.CreateClient();
            var tags     = client.Repository.GetAllTags(repoPath.Owner, repoPath.Repo).Result;
            var tag      = tags.SingleOrDefault(tag => tag.Name == tagName);

            if (tag == null)
            {
                if (string.IsNullOrEmpty(branch))
                {
                    // Identify the default branch.

                    var branches = client.Repository.Branch.GetAll(repoPath.Owner, repoPath.Repo).Result;

                    foreach (var branchDetails in branches)
                    {
                        if (branchDetails.Name == "master")
                        {
                            branch = "master";
                            break;
                        }
                        else if (branchDetails.Name == "main")
                        {
                            branch = "main";
                            break;
                        }
                    }
                }
            }

            // Create the release.

            var release = new NewRelease(tagName)
            {
                Name       = releaseName,
                Draft      = draft,
                Prerelease = prerelease,
                Body       = body
            };

            return client.Repository.Release.Create(repoPath.Owner, repoPath.Repo, release).Result;
        }

        /// <summary>
        /// Updates a GitHub release.
        /// </summary>
        /// <param name="repo">Identifies the target repository.</param>
        /// <param name="release">Specifies the release being updated.</param>
        /// <param name="releaseUpdate">Specifies the revisions.</param>
        /// <returns>The updated release.</returns>
        /// <remarks>
        /// <para>
        /// To update a release, you'll first need to:
        /// </para>
        /// <list type="number">
        /// <item>
        /// Obtain a <see cref="Release"/> referencing the target release returned from 
        /// <see cref="Create(string, string, string, string, bool, bool, string)"/>
        /// or by listing or getting releases.
        /// </item>
        /// <item>
        /// Obtain a <see cref="ReleaseUpdate"/> by calling <see cref="Release.ToUpdate"/>.
        /// </item>
        /// <item>
        /// Make your changes to the release update.
        /// </item>
        /// <item>
        /// Call <see cref="Update(string, Release, ReleaseUpdate)"/>, passing the 
        /// original release along with the update.
        /// </item>
        /// </list>
        /// </remarks>
        public Release Update(string repo, Release release, ReleaseUpdate releaseUpdate)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));
            Covenant.Requires<ArgumentNullException>(release != null, nameof(release));
            Covenant.Requires<ArgumentNullException>(releaseUpdate != null, nameof(releaseUpdate));

            var repoPath = GitHubRepoPath.Parse(repo);
            var client   = GitHub.CreateClient();

            return client.Repository.Release.Edit(repoPath.Owner, repoPath.Repo, release.Id, releaseUpdate).Result;
        }

        /// <summary>
        /// List the releases for a GitHub repo.
        /// </summary>
        /// <returns>The list of releases.</returns>
        public IReadOnlyList<Release> List(string repo)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));

            var repoPath = GitHubRepoPath.Parse(repo);
            var client   = GitHub.CreateClient();

            return client.Repository.Release.GetAll(repoPath.Owner, repoPath.Repo).Result;
        }

        /// <summary>
        /// Retrieves a specific GitHub release.
        /// </summary>
        /// <param name="repo">Identifies the target repository.</param>
        /// <param name="tagName">Specifies the tag for the target release.</param>
        /// <returns>The release information or <c>null</c> when the requested release doesn't exist.</returns>
        public Release Get(string repo, string tagName)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(tagName), nameof(tagName));

            var repoPath = GitHubRepoPath.Parse(repo);
            var client   = GitHub.CreateClient();

            try
            {
                return client.Repository.Release.Get(repoPath.Owner, repoPath.Repo, tagName).Result;
            }
            catch (Exception e)
            {
                if (e.Find<NotFoundException>() != null)
                {
                    return null;
                }

                throw;
            }
        }

        /// <summary>
        /// Returns the releases that satisfies a predicate.
        /// </summary>
        /// <param name="repo">Identifies the target repository.</param>
        /// <param name="predicate">The predicate.</param>
        /// <returns>The list of matching releases.</returns>
        public List<Release> Find(string repo, Func<Release, bool> predicate)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));
            Covenant.Requires<ArgumentNullException>(predicate != null, nameof(predicate));

            var repoPath = GitHubRepoPath.Parse(repo);
            var client   = GitHub.CreateClient();

            return List(repo).Where(predicate).ToList();
        }

        /// <summary>
        /// Uploads an asset file to a GitHub release.  Any existing asset with same name will be replaced.
        /// </summary>
        /// <param name="repo">Identifies the target repository.</param>
        /// <param name="release">The target release.</param>
        /// <param name="assetPath">Path to the source asset file.</param>
        /// <param name="assetName">Optionally specifies the file name to assign to the asset.  This defaults to the file name in <paramref name="assetPath"/>.</param>
        /// <param name="contentType">Optionally specifies the asset's <b>Content-Type</b>.  This defaults to: <b> application/octet-stream</b></param>
        /// <returns>The new <see cref="ReleaseAsset"/>.</returns>
        /// <exception cref="NotSupportedException">Thrown when the releas has already been published.</exception>
        /// <remarks>
        /// <note>
        /// The current implementation only works for unpublished releases where <c>Draft=true</c>.
        /// </note>
        /// </remarks>
        public ReleaseAsset UploadAsset(string repo, Release release, string assetPath, string assetName = null, string contentType = "application/octet-stream")
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));
            Covenant.Requires<ArgumentNullException>(release != null, nameof(release));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(assetPath), nameof(assetPath));

            if (!release.Draft)
            {
                throw new NotSupportedException("Cannot upload asset to already published release.");
            }

            var repoPath = GitHubRepoPath.Parse(repo);
            var client   = GitHub.CreateClient();

            using (var assetStream = File.OpenRead(assetPath))
            {
                if (string.IsNullOrEmpty(assetName))
                {
                    assetName = Path.GetFileName(assetPath);
                }

                var upload = new ReleaseAssetUpload()
                {
                    FileName    = assetName,
                    ContentType = contentType,
                    RawData     = assetStream
                };

                return client.Repository.Release.UploadAsset(release, upload).Result;
            }
        }

        /// <summary>
        /// Uploads an asset stream to a GitHub release.  Any existing asset with same name will be replaced.
        /// </summary>
        /// <param name="repo">Identifies the target repository.</param>
        /// <param name="release">The target release.</param>
        /// <param name="assetStream">The asset source stream.</param>
        /// <param name="assetName">Specifies the file name to assign to the asset.</param>
        /// <param name="contentType">Optionally specifies the asset's <b>Content-Type</b>.  This defaults to: <b> application/octet-stream</b></param>
        /// <returns>The new <see cref="ReleaseAsset"/>.</returns>
        public ReleaseAsset UploadAsset(string repo, Release release, Stream assetStream, string assetName, string contentType = "application/octet-stream")
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));
            Covenant.Requires<ArgumentNullException>(release != null, nameof(release));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(assetName), nameof(assetName));
            Covenant.Requires<ArgumentNullException>(assetStream != null, nameof(assetStream));

            var client  = GitHub.CreateClient();

            var upload = new ReleaseAssetUpload()
            {
                FileName    = assetName,
                ContentType = contentType,
                RawData     = assetStream
            };

            return client.Repository.Release.UploadAsset(release, upload).Result;
        }

        /// <summary>
        /// <para>
        /// Returns the URI that can be used to download a GitHub release asset.
        /// </para>
        /// <note>
        /// This works only for published releases.
        /// </note>
        /// </summary>
        /// <param name="release">The target release.</param>
        /// <param name="asset">The target asset.</param>
        /// <returns>The asset URI.</returns>
        public string GetAssetUri(Release release, ReleaseAsset asset)
        {
            Covenant.Requires<ArgumentNullException>(release != null, nameof(release));
            Covenant.Requires<ArgumentNullException>(asset != null, nameof(asset));

            var releasedAsset = release.Assets.SingleOrDefault(a => a.Id == asset.Id);

            if (releasedAsset == null)
            {
                throw new DeploymentException($"Asset [id={asset.Id}] is not present in release [id={release.Id}].");
            }

            return releasedAsset.BrowserDownloadUrl;
        }

        /// <summary>
        /// Deletes a GitHub release.
        /// </summary>
        /// <param name="repo">Identifies the target repository.</param>
        /// <param name="release">The target release.</param>
        /// <remarks>
        /// <note>
        /// This fails silently if the release doesn't exist.
        /// </note>
        /// </remarks>
        public void Remove(string repo, Release release)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));
            Covenant.Requires<ArgumentNullException>(release != null, nameof(release));

            var repoPath = GitHubRepoPath.Parse(repo);
            var client   = GitHub.CreateClient();

            client.Repository.Release.Delete(repoPath.Owner, repoPath.Repo, release.Id).WaitWithoutAggregate();
        }

        /// <summary>
        /// Uploads a multi-part download to a release and then publishes the release.
        /// </summary>
        /// <param name="repo">Identifies the target repository.</param>
        /// <param name="release">The target release.</param>
        /// <param name="sourcePath">Path to the file being uploaded.</param>
        /// <param name="version">The download version.</param>
        /// <param name="name">Optionally overrides the download file name specified by <paramref name="sourcePath"/> to initialize <see cref="DownloadManifest.Name"/>.</param>
        /// <param name="filename">Optionally overrides the download file name specified by <paramref name="sourcePath"/> to initialize <see cref="DownloadManifest.Filename"/>.</param>
        /// <param name="noMd5File">
        /// This method creates a file named [<paramref name="sourcePath"/>.md5] with the MD5 hash for the entire
        /// uploaded file by default.  You may override this behavior by passing <paramref name="noMd5File"/>=<c>true</c>.
        /// </param>
        /// <param name="maxPartSize">Optionally overrides the maximum part size (defaults to 75 MiB).</param>d
        /// <returns>The <see cref="DownloadManifest"/>.</returns>
        /// <remarks>
        /// <para>
        /// The release passed must be unpublished and you may upload other assets before calling this.
        /// </para>
        /// <note>
        /// Take care that any assets already published have names that won't conflict with the asset
        /// part names, which will be formatted like: <b>part-##</b>
        /// </note>
        /// <note>
        /// Unlike the S3 implementation, this method uploads the parts to GitHub on a single thread.
        /// </note>
        /// </remarks>
        public DownloadManifest UploadMultipartAsset(
            string      repo,
            Release     release, 
            string      sourcePath, 
            string      version, 
            string      name        = null,
            string      filename    = null,
            bool        noMd5File   = false,
            long        maxPartSize = (long)(75 * ByteUnits.MebiBytes))
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(repo), nameof(repo));
            Covenant.Requires<ArgumentNullException>(release != null, nameof(release));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(sourcePath), nameof(sourcePath));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(version), nameof(version));

            name     = name ?? Path.GetFileName(sourcePath);
            filename = filename ?? Path.GetFileName(sourcePath);

            // We're going to use two streams here, one to compute the part MD5
            // and the other to actually upload the part to S3 and we're going to
            // do this in parallel threads to increase the throughput.

            var manifest   = new DownloadManifest() { Name = name, Version = version, Filename = filename };
            var assetParts = new List<ReleaseAsset>();

            var uploadTask = Task.Run(
                () =>
                {
                    using (var input = File.OpenRead(sourcePath))
                    {
                        if (input.Length == 0)
                        {
                            throw new IOException($"Asset at [{sourcePath}] cannot be empty.");
                        }

                        var partCount   = NeonHelper.PartitionCount(input.Length, maxPartSize);
                        var partNumber  = 0;
                        var partStart   = 0L;
                        var cbRemaining = input.Length;

                        while (cbRemaining > 0)
                        {
                            var partSize = Math.Min(cbRemaining, maxPartSize);

                            using (var uploadPartStream = new SubStream(input, partStart, partSize))
                            {
                                var asset = GitHub.Releases.UploadAsset(repo, release, uploadPartStream, $"part-{partNumber:0#}");

                                assetParts.Add(asset);
                            }

                            // Loop to handle the next part (if any).

                            partNumber++;
                            partStart   += partSize;
                            cbRemaining -= partSize;
                        }
                    }
                });

            var md5Task = Task.Run(
                () =>
                {
                    using (var input = File.OpenRead(sourcePath))
                    {
                        if (input.Length == 0)
                        {
                            throw new IOException($"Asset at [{sourcePath}] cannot be empty.");
                        }

                        var partCount   = NeonHelper.PartitionCount(input.Length, maxPartSize);
                        var partNumber  = 0;
                        var partStart   = 0L;
                        var cbRemaining = input.Length;

                        manifest.Md5   = CryptoHelper.ComputeMD5String(input);
                        input.Position = 0;

                        while (cbRemaining > 0)
                        {
                            var partSize = Math.Min(cbRemaining, maxPartSize);
                            var part     = new DownloadPart()
                            {
                                Number = partNumber,
                                Size   = partSize,
                            };

                            using (var md5PartStream = new SubStream(input, partStart, partSize))
                            {
                                part.Md5 = CryptoHelper.ComputeMD5String(md5PartStream);
                            }

                            manifest.Parts.Add(part);

                            // Loop to handle the next part (if any).

                            partNumber++;
                            partStart   += partSize;
                            cbRemaining -= partSize;
                        }

                        manifest.Size = manifest.Parts.Sum(part => part.Size);
                    }
                });

            uploadTask.Wait();
            md5Task.Wait();

            // Publish the release.

            var releaseUpdate = release.ToUpdate();

            releaseUpdate.Draft = false;

            release = GitHub.Releases.Update(repo, release, releaseUpdate);

            // Now that the release has been published, we can go back and fill in
            // the asset URIs for each of the download parts.

            for (int partNumber = 0; partNumber < manifest.Parts.Count; partNumber++)
            {
                manifest.Parts[partNumber].Uri = GitHub.Releases.GetAssetUri(release, assetParts[partNumber]);
            }

            // Write the MD5 file unless disabled.

            if (!noMd5File)
            {
                File.WriteAllText($"{sourcePath}.md5", manifest.Md5);
            }

            return manifest;
        }
    }
}
