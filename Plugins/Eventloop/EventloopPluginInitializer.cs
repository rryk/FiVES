﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FIVES;

namespace EventloopPlugin
{
    public class EventloopPluginInitializer : IPluginInitializer
    {
        public string Name
        {
            get { return "Eventloop"; }
        }

        public List<string> PluginDependencies
        {
            get { return new List<string> { }; }
        }

        public List<string> ComponentDependencies
        {
            get { return new List<string> { }; }
        }

        public void Initialize()
        {
            Eventloop.Instance = new Eventloop();
        }
    }
}
