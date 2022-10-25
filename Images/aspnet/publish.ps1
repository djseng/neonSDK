﻿#Requires -Version 7.1.3 -RunAsAdministrator
#------------------------------------------------------------------------------
# FILE:         publish.ps1
# CONTRIBUTOR:  Marcus Bowyer
# COPYRIGHT:    Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# Builds the aspnet images and pushes them to Docker Hub.
#
# NOTE: You must be already logged into the target container registry.
#
# USAGE: pwsh -f publish.ps1 [-all]

param 
(
	[switch]$allVersions = $false,
    [switch]$nopush = $false
)

#----------------------------------------------------------
# Global Includes
$image_root = "$env:NF_ROOT\Images"
. $image_root/includes.ps1
#----------------------------------------------------------

function Build
{
	param
	(
		[parameter(Mandatory=$true, Position=1)][string] $version,
		[switch]$latest = $false
	)

	$registry    = GetSdkRegistry "aspnet"
	$tagAsLatest = TagAsLatest
	$tagOverride = $env:DEBUG_TAG

	if (![string]::IsNullOrEmpty($tagOverride))
	{
		$tag    = $tagOverride
		$latest = $false
	}

	Log-ImageBuild $registry $version

	# Build and publish the images.

	. ./build.ps1 -registry $registry -version $version
    Push-DockerImage "${registry}:${version}"

	if ($latest -and $tagAsLatest)
	{
		$result = Invoke-CaptureStreams "docker tag ${registry}:${version} ${registry}:latest" -interleave
		Push-DockerImage ${registry}:latest
	}
}

$noImagePush = $nopush

try
{
	if ($allVersions)
	{
		Build "6.0.9-jammy-amd64"
		Build "6.0.10-jammy-amd64"
	}

	Build "7.0.0-rc.2-jammy-amd64" -latest
}
catch
{
	Write-Exception $_
	exit 1
}
