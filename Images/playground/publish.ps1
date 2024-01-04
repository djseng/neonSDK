#Requires -Version 7.1.3 -RunAsAdministrator
#------------------------------------------------------------------------------
# FILE:         publish.ps1
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:    Copyright © 2005-2024 by NEONFORGE LLC.  All rights reserved.
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

# Builds the Playground image and pushes it to the container registry.

param 
(
	[parameter(Mandatory=$true, Position=1)]
    [string]$config,
	[switch]$allVersions = $false,
    [switch]$nopush      = $false
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

	$registry    = GetSdkRegistry "playground"
	$tag         = $version
	$tagAsLatest = TagAsLatest

	Log-ImageBuild $registry $tag

	# Build and publish the images.

	. ./build.ps1 -registry $registry -version $version -tag $tag
    Push-DockerImage "${registry}:${tag}"

	if ($latest -and $tagAsLatest)
	{
		Invoke-CaptureStreams "docker tag ${registry}:${tag} ${registry}:latest" -interleave | Out-Null
		Push-DockerImage "${registry}:latest"
	}
}

$noImagePush = $nopush

try
{
	if ($allVersions)
	{
	}

	Build "0" -latest
}
catch
{
	Write-Exception $_
	exit 1
}
