﻿// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// 
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XboxLiveResiliencyPluginForFiddler
{
    public partial class About : Form
    {
        public About()
        {
            InitializeComponent();
            VersionLabel.Text = String.Format("Version: {0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
        }
    }
}
