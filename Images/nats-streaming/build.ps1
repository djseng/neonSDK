#Requires -Version 7.1.3 -RunAsAdministrator
#------------------------------------------------------------------------------
# FILE:         build.ps1
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

# Builds the NATS-STREAMING image.
#
# USAGE: pwsh -f build.ps1 REGISTRY VERSION TAG

param 
(
	[parameter(Mandatory=$true, Position=1)][string] $registry,
	[parameter(Mandatory=$true, Position=2)][string] $version,
	[parameter(Mandatory=$true, Position=3)][string] $tag
)

Pull-ContainerImage "nats-streaming:$version-linux"
Invoke-CaptureStreams "docker build -t ${registry}:${tag} --build-arg VERSION=$version ." -interleave | Out-Null
