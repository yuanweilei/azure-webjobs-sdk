﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Host.Hosting
{
    public class WebJobsExtensionOptionSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new WebJobsExtensionOptionProvider();
    }
}
