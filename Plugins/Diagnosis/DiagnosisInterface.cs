﻿using FIVES;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace DiagnosisPlugin
{
    public class DiagnosisInterface
    {
        private static DiagnosisInterface instance;

        public PluginContext[] Contexts { get { return registeredPlugins.Values.ToArray(); } }
        public XmlDocument WidgetTemplate { get; private set; }

        public static DiagnosisInterface Instance
        {
            get
            {
                if (instance == null)
                    instance = new DiagnosisInterface();
                return instance;
            }
        }

        private DiagnosisInterface()
        {
            WidgetTemplate = new XmlDocument();
            WidgetTemplate.Load("DiagnosisWebpage/dynamic/widget_template.html");
        }

        public PluginContext IntroduceSelf(IDiagnosablePlugin self)
        {
            PluginContext context = new PluginContext(self);
            registeredPlugins.Add(self.Name, context);
            return context;
        }

        // POST /diagnosis/action/$PLUGINNAME/$METHODNAME ; [params]
        public void CallPluginMethod(string pluginName, string methodName, string[] parameters)
        {
            registeredPlugins[pluginName].CallMethod(methodName, parameters);
        }

        Dictionary<string, PluginContext> registeredPlugins = new Dictionary<string, PluginContext>();

    }
}
