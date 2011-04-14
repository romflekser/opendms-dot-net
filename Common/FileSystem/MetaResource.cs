﻿/* Copyright 2011 the OpenDMS.NET Project (http://sites.google.com/site/opendmsnet/)
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;

namespace Common.FileSystem
{
    /// <summary>
    /// Represents a <see cref="Data.MetaAsset"/> on the local file system.
    /// </summary>
    public class MetaResource 
        : ResourceBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MetaResource"/> class.
        /// </summary>
        /// <param name="guid">A <see cref="Guid"/> providing a unique reference to the Asset.</param>
        /// <param name="fileSystem">A reference to the <see cref="FileSystem.IO"/> instance.</param>
        public MetaResource(Guid guid, IO fileSystem)
            : base(guid, ResourceType.Meta, ".xml", fileSystem)
        {
        }
    }
}