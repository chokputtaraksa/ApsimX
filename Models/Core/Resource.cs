﻿namespace Models.Core
{
    using APSIM.Shared.Utilities;
    using Models.Core.ApsimFile;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// This class encapsulates an instruction to replace a model.
    /// </summary>
    public class Resource
    {
        /// <summary>Properties to exclude from the doc.</summary>
        private static string[] propertiesToExclude = new string[] { "Name", "Children", "IsHidden", "IncludeInDocumentation", "Enabled", "ReadOnly" };

        /// <summary>The instance of resource.</summary>
        private static Resource instance = null;

        /// <summary>A cache of models from resource.</summary>
        private readonly Dictionary<string, ResourceModel> cache = new Dictionary<string, ResourceModel>();

        /// <summary>A lock for the cache.</summary>
        private readonly object cacheLock = new object();

        /// <summary>Singleton instance of Resource</summary>
        public static Resource Instance
        {
            get
            {
                if (instance == null)
                    instance = new Resource();
                return instance;
            }
        }

        /// <summary>Replace all child models (of a parent) that have ResourceName with a new model loaded from a resource.</summary>
        /// <param name="parent">The parent model to search for children.</param>
        public void Replace(IModel parent)
        {
            foreach (var child in parent.FindAllDescendants()
                                        .Where(m => !string.IsNullOrEmpty(m.ResourceName))   // non-empty resourcename
                                        .ToArray()) // ToArray is necessary to stop 'Collection was modified' exception
            {
                IModel modelFromResource = GetModel(child.ResourceName);
                if (modelFromResource != null)
                {
                    modelFromResource.Enabled = parent.Enabled;

                    bool isUnderReplacements = child.FindAncestor<Folder>("Replacements") != null;

                    // Add missing children from the model in resource
                    IEnumerable<IModel> childrenToAdd = modelFromResource.Children.Where(mc =>
                    {
                        return !child.Children.Any(c => c.GetType() == mc.GetType() &&
                                                        string.Equals(c.Name, mc.Name, StringComparison.InvariantCultureIgnoreCase));
                    });
                    
                    // Make all children that area about to be added from resource hidden and readonly.
                    bool isHidden = !isUnderReplacements;
                    foreach (Model descendant in childrenToAdd)
                    {
                        descendant.IsHidden = isHidden;
                        descendant.ReadOnly = isHidden;
                    }

                    child.Children.InsertRange(0, childrenToAdd);

                    CopyPropertiesFrom(modelFromResource, child);
                    child.ParentAllDescendants();
                }
            }
        }

        /// <summary>Get a model from resource.</summary>
        /// <param name="resourceName">Name of model.</param>
        /// <returns>The newly created model. Throws if not found.</returns>
        public IModel GetModel(string resourceName)
        {
            var resourceModel = GetModelNoClone(resourceName);
            return resourceModel?.Model.Clone();
        }

        /// <summary>Remove all children that are from a resource.</summary>
        /// <param name="model">The model to remove child models from.</param>
        public IEnumerable<IModel> RemoveResourceChildren(IModel model)
        {
            if (string.IsNullOrEmpty(model.ResourceName))
                return model.Children;
            else
            {
                var resourceModel = GetModelNoClone(model.ResourceName);
                return model.Children.Where(m => !resourceModel.Model.Children.Any(rc => m.GetType() == rc.GetType() &&
                                                                                         m.Name.Equals(rc.Name, StringComparison.InvariantCultureIgnoreCase)));
            }
        }

        /// <summary>Get a model resource as a string.</summary>
        /// <param name="resourceName">Name of the resource.</param>
        /// <returns>The model JSON string. Throws if not found.</returns>
        public static string GetString(string resourceName)
        {
            string fullResourceName = $"Models.Resources.{resourceName}.json";
            var contents = ReflectionUtilities.GetResourceAsString(fullResourceName);
            return contents;
        }

        /// <summary>Get a collection of all properties from the specified resource model.</summary>
        /// <param name="resourceName">Name of the resource.</param>
        public IEnumerable<PropertyInfo> GetPropertiesFromResourceModel(string resourceName)
        {
            return GetModelNoClone(resourceName)?.Properties;
        }

        /// <summary>
        /// Get a list of parameter names for this model.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> GetModelParameterNames(string resourceName)
        {
            string contents = Resource.GetString(resourceName);
            if (contents != null)
            {
                var parameterNames = new List<string>();

                var json = JObject.Parse(contents);
                var children = json["Children"] as JArray;
                var simulations = children[0];

                GetParametersFromToken(simulations, null, parameterNames);
                return parameterNames;
            }

            return null;
        }

        /// <summary>Default constructor (private)</summary>
        private Resource() { }

        /// <summary>Get a model from resource.</summary>
        /// <param name="resourceName">Name of model.</param>
        /// <returns>The newly created model. Throws if not found.</returns>
        private ResourceModel GetModelNoClone(string resourceName)
        {
            if (!cache.TryGetValue(resourceName, out ResourceModel modelFromResource))
            {
                lock (cacheLock)
                {
                    if (!cache.TryGetValue(resourceName, out modelFromResource))
                    {
                        string contents = GetString(resourceName);
                        if (string.IsNullOrEmpty(contents))
                            return null;

                        modelFromResource = new ResourceModel(contents);
                        cache.Add(resourceName, modelFromResource);
                    }
                }
            }
            return modelFromResource;
        }

        /// <summary>Copy all public properties from the one model to another.</summary>
        /// <param name="from">Model to copy from.</param>
        /// <param name="to">Model to copy to.</param>
        private void CopyPropertiesFrom(IModel from, IModel to)
        {
            foreach (PropertyInfo property in GetPropertiesFromResourceModel(to.ResourceName))
            {
                try
                {
                    object fromValue = property.GetValue(from);
                    bool doSetPropertyValue;
                    if (fromValue is double)
                        doSetPropertyValue = Convert.ToDouble(fromValue, CultureInfo.InvariantCulture) != 0;
                    else
                        doSetPropertyValue = fromValue != null;

                    if (doSetPropertyValue)
                        property.SetValue(to, fromValue);
                }
                catch (Exception)
                {
                    // Couldn't set property - ignore error.
                }
            }
        }

        /// <summary>
        /// Get a list of parameter names for the specified token.
        /// </summary>
        /// <param name="token">The token to extract parameter names from.</param>
        /// <param name="namePrefix">The prefix to add in front of each name.</param>
        /// <param name="parameterNames">The list of parameter names to add to.</param>
        private static void GetParametersFromToken(JToken token, string namePrefix, List<string> parameterNames)
        {
            foreach (var parameter in token.Children())
            {
                if (parameter is JProperty)
                {
                    var property = parameter as JProperty;
                    if (property.Name == "Children" && property.First is JArray)
                    {
                        var children = property.First as JArray;
                        foreach (var child in children)
                            GetParametersFromToken(child, namePrefix + child["Name"] + ".", parameterNames); // recursion
                    }
                    else if (property.Name != "$type" && !propertiesToExclude.Contains(property.Name))
                        parameterNames.Add(namePrefix + property.Name);
                }
            }
        }

        /// <summary>Encapsulates a model from resources.</summary>
        private class ResourceModel
        {
            /// <summary>Constructor.</summary>
            /// <param name="resourceJson">The resource JSON.</param>
            public ResourceModel(string resourceJson)
            {
                Model = FileFormat.ReadFromString<IModel>(resourceJson, e => throw e, false);
                Model = Model.Children.First();
                Properties = GetPropertiesFromResourceModel(Model, resourceJson);
            }

            /// <summary>The model deserialised from resource.</summary>
            public IModel Model { get; }

            /// <summary>The properties of the model from resource.</summary>
            public IEnumerable<PropertyInfo> Properties { get; }

            /// <summary>Get a collection of all properties from the specified resource model.</summary>
            /// <param name="model">The model.</param>
            /// <param name="resourceJson">The resource JSON.</param>
            private IEnumerable<PropertyInfo> GetPropertiesFromResourceModel(IModel model, string resourceJson)
            {
                string[] propertiesNotToCopy = { "$type", "Name", "Parent", "Children", "IncludeInDocumentation", "ResourceName", "Enabled", "ReadOnly" };

                List<PropertyInfo> properties = new List<PropertyInfo>();
                
                var children = JObject.Parse(resourceJson)["Children"] as JArray;
                if (children == null)
                    throw new Exception($"Invalid resource {model.Name}");

                var resourceToken = children[0] as JObject;
                var propertyTokens = resourceToken.Properties();
                foreach (var propertyName in resourceToken.Properties()
                                                            .Select(pt => pt.Name)
                                                            .Where(name => !propertiesNotToCopy.Contains(name)))
                {
                    var propertyInfo = model.GetType().GetProperty(propertyName);
                    if (propertyInfo != null &&
                        propertyInfo.CanWrite &&
                        propertyInfo.CanRead &&
                        propertyInfo.GetCustomAttribute(typeof(DescriptionAttribute)) == null &&
                        propertyInfo.GetCustomAttribute(typeof(JsonIgnoreAttribute)) == null)
                        properties.Add(propertyInfo);
                }

                return properties;
            }
        }
    }
}
