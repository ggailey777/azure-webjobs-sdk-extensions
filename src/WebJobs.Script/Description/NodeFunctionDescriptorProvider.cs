﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class NodeFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private ScriptHost _host;
        private readonly ScriptHostConfiguration _config;
        private readonly string _rootPath;

        public NodeFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
        {
            _host = host;
            _config = config;
            _rootPath = config.RootPath;
        }

        public override bool TryCreate(FunctionFolderInfo functionFolderInfo, out FunctionDescriptor functionDescriptor)
        {
            functionDescriptor = null;

            // name might point to a single file, or a module
            string extension = Path.GetExtension(functionFolderInfo.Source).ToLower();
            if (!(extension == ".js" || string.IsNullOrEmpty(extension)))
            {
                return false;
            }

            // parse the bindings
            JObject bindings = (JObject)functionFolderInfo.Configuration["bindings"];
            JArray inputs = (JArray)bindings["input"];
            Collection<Binding> inputBindings = Binding.GetBindings(_config, inputs, FileAccess.Read);

            JArray outputs = (JArray)bindings["output"];
            Collection<Binding> outputBindings = Binding.GetBindings(_config, outputs, FileAccess.Write);

            JObject trigger = (JObject)inputs.FirstOrDefault(p => ((string)p["type"]).ToLowerInvariant().EndsWith("trigger"));

            // A function can be disabled at the trigger or function level
            if (IsDisabled(functionFolderInfo.Name, trigger) ||
                IsDisabled(functionFolderInfo.Name, functionFolderInfo.Configuration))
            {
                return false;
            }

            string triggerType = (string)trigger["type"];
            string triggerParameterName = (string)trigger["name"];
            if (string.IsNullOrEmpty(triggerParameterName))
            {
                // default the name to simply 'input'
                trigger["name"] = triggerParameterName = "input";
            }

            NodeFunctionInvoker invoker = new NodeFunctionInvoker(_host, triggerParameterName, functionFolderInfo, inputBindings, outputBindings);

            ParameterDescriptor triggerParameter = null;
            switch (triggerType)
            {
                case "queueTrigger":
                    triggerParameter = ParseQueueTrigger(trigger);
                    break;
                case "blobTrigger":
                    triggerParameter = ParseBlobTrigger(trigger);
                    break;
                case "serviceBusTrigger":
                    triggerParameter = ParseServiceBusTrigger(trigger);
                    break;
                case "timerTrigger":
                    triggerParameter = ParseTimerTrigger(trigger, typeof(TimerInfo));
                    break;
                case "webHookTrigger":
                    triggerParameter = ParseWebHookTrigger(trigger);
                    break;
            }

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor("log", typeof(TraceWriter)));

            // Add an IBinder to support the binding programming model
            parameters.Add(new ParameterDescriptor("binder", typeof(IBinder)));

            functionDescriptor = new FunctionDescriptor(functionFolderInfo.Name, invoker, parameters);

            return true;
        }
    }
}