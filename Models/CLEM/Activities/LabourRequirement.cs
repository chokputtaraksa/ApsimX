using Models.Core;
using Models.CLEM.Activities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.DataAnnotations;
using Models.CLEM.Groupings;
using Models.CLEM.Resources;
using Models.Core.Attributes;
using System.IO;
using System.Text.Json.Serialization;
using Models.CLEM.Interfaces;
using APSIM.Shared.Utilities;

namespace Models.CLEM.Activities
{
    ///<summary>
    /// Defines the labour required for an activity
    ///</summary> 
    [Serializable]
    [ViewName("UserInterface.Views.PropertyCategorisedView")]
    [PresenterName("UserInterface.Presenters.PropertyCategorisedPresenter")]
    [ValidParent(ParentType = typeof(IHandlesActivityCompanionModels))]
    [Description("Defines the amount and type of labour required for an activity. This component must have at least one LabourFilterGroup as a child")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Labour/LabourRequirement.htm")]
    public class LabourRequirement: CLEMActivityBase, IValidatableObject, IActivityCompanionModel, IReportPartialResourceAction
    {
        private double maximumDaysPerPerson = 0;
        private double maximumDaysPerGroup = 0;
        private double minimumDaysPerPerson = 0;
        private Labour labourResource;
        private List<ResourceRequest> resourceList = new List<ResourceRequest>();

        /// <summary>
        /// Constructor
        /// </summary>
        public LabourRequirement()
        {
            AllocationStyle = ResourceAllocationStyle.Manual;
            base.ModelSummaryStyle = HTMLSummaryStyle.SubResource;
            this.SetDefaults();
        }

        /// <summary>
        /// An identifier for this Labour requirement based on parent requirements
        /// </summary>
        [Description("Labour identifier")]
        [Core.Display(Type = DisplayType.DropDown, Values = "ParentSuppliedIdentifiers", VisibleCallback = "ParentSuppliedIdentifiersPresent")]
        public string Identifier { get; set; }

        /// <summary>
        /// Days labour required per unit or fixed (days)
        /// </summary>
        [Description("Days labour required [per unit or fixed]")]
        [Required, GreaterThanEqualValue(0)]
        [Category("Labour", "Rate")]
        public double LabourPerUnit { get; set; }

        /// <summary>
        /// Size of unit
        /// </summary>
        [Description("Number of units")]
        [Required, GreaterThanEqualValue(0)]
        [Category("Labour", "Units")]
        public double UnitSize { get; set; }

        /// <summary>
        /// Whole unit blocks only
        /// </summary>
        [Description("Request as whole unit blocks only")]
        [Category("Labour", "Units")]
        public bool WholeUnitBlocks { get; set; }

        /// <summary>
        /// Labour unit type
        /// </summary>
        [Core.Display(Type = DisplayType.DropDown, Values = "ParentSuppliedMeasures", VisibleCallback = "ParentSuppliedMeasuresPresent")]
        [Description("Measure to use")]
        [Category("Labour", "Units")]
        public string Measure { get; set; }

        /// <summary>
        /// Labour limit style
        /// </summary>
        [Description("Limit style")]
        [System.ComponentModel.DefaultValueAttribute(LabourLimitType.ProportionOfDaysRequired)]
        [Category("Labour", "Limits")]
        [Required]
        public LabourLimitType LimitStyle { get; set; }

        /// <summary>
        /// Maximum labour allocated per labour group
        /// </summary>
        [Description("Maximum per group for task")]
        [Required, GreaterThanValue(0)]
        [Category("Labour", "Limits")]
        [System.ComponentModel.DefaultValueAttribute(1)]
        public double MaximumPerGroup { get; set; }

        /// <summary>
        /// Minimum labour allocated per person for task
        /// </summary>
        [Description("Minimum per person for task")]
        [Required, GreaterThanEqualValue(0)]
        [Category("Labour", "Limits")]
        public double MinimumPerPerson { get; set; }

        /// <summary>
        /// Maximum labour allocated per person for task
        /// </summary>
        [Description("Maximum per person for task")]
        [Required, GreaterThanValue(0), GreaterThan("MinimumPerPerson", ErrorMessage ="Maximum per individual must be greater than minimum per individual in Labour Required")]
        [Category("Labour", "Limits")]
        public double MaximumPerPerson { get; set; }

        /// <summary>
        /// Apply to all matching labour (everyone performs activity)
        /// </summary>
        [Description("Apply to all matching (everyone performs activity)")]
        [Required]
        [Category("Labour", "Rate")]
        public bool ApplyToAll { get; set; }

        /// <summary>
        /// Get the calculated maximum days per person for activity from CalculateLimits
        /// </summary>
        [JsonIgnore]
        public double MaximumDaysPerPerson { get { return maximumDaysPerPerson; } }

        /// <summary>
        /// Get the calculated maximum days per person for activity from CalculateLimits
        /// </summary>
        [JsonIgnore]
        public double MaximumDaysPerGroup { get { return maximumDaysPerGroup; } }

        /// <summary>
        /// Get the calculated maximum days per person for activity from CalculateLimits
        /// </summary>
        [JsonIgnore]
        public double MinimumDaysPerPerson { get { return minimumDaysPerPerson; } }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            labourResource = Resources.FindResourceGroup<Labour>();
        }

        /// <summary>
        /// Calcuate the limits for people and groups using the style
        /// </summary>
        public void CalculateLimits(double amountRequested)
        {
            switch (LimitStyle)
            {
                case LabourLimitType.AsDaysRequired:
                    double units = amountRequested / UnitSize / LabourPerUnit;
                    maximumDaysPerPerson = units * MaximumPerPerson;
                    maximumDaysPerGroup = units * MaximumPerGroup;
                    minimumDaysPerPerson = units * MinimumPerPerson;
                    break;
                case LabourLimitType.AsTotalAllowed:
                    maximumDaysPerPerson = MaximumPerPerson;
                    maximumDaysPerGroup = MaximumPerGroup;
                    minimumDaysPerPerson = MinimumPerPerson;
                    break;
                case LabourLimitType.ProportionOfDaysRequired:
                    maximumDaysPerPerson = amountRequested * MaximumPerPerson;
                    maximumDaysPerGroup = amountRequested * MaximumPerGroup;
                    minimumDaysPerPerson = amountRequested * MinimumPerPerson;
                    break;
                default:
                    break;
            }
            return;            
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double activityMetric)
        {
            resourceList.Clear();
            IEnumerable<LabourType> labourers = labourResource?.Items.Where(a => a.Hired != true);
            double daysNeeded = 0;
            double numberUnits = 0;

            switch (Measure)
            {
                case "Fixed":
                    daysNeeded = LabourPerUnit * activityMetric;
                    break;
                default:
                    numberUnits = activityMetric / UnitSize;
                    if (WholeUnitBlocks)
                        numberUnits = Math.Ceiling(numberUnits);
                    daysNeeded = numberUnits * LabourPerUnit;
                    break;
            }

            if (MathUtilities.IsPositive(daysNeeded))
            {
                CLEMActivityBase handlesActivityComponents = this.Parent as CLEMActivityBase;

                foreach (LabourGroup fg in FindAllChildren<LabourGroup>())
                {
                    int numberOfPpl = 1;
                    if (ApplyToAll)
                        // how many matches
                        numberOfPpl = fg.Filter(labourResource.Items).Count();
                    for (int i = 0; i < numberOfPpl; i++)
                    {
                        resourceList.Add(new ResourceRequest()
                        {
                            AllowTransmutation = true,
                            Required = daysNeeded,
                            ResourceType = typeof(Labour),
                            ResourceTypeName = "",
                            ActivityModel = this,
                            FilterDetails = new List<object>() { fg },
                            Category = this.TransactionCategory,
                        }
                        ); ;
                    }
                }
            }
            return resourceList;
        }

        #region validation

        /// <summary>
        /// Validate this object
        /// </summary>
        /// <param name="validationContext"></param>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();
            // ensure labour resource added
            Labour lab = Resources.FindResource<Labour>();
            if (lab == null)
                Summary.WriteMessage(this, "No [r=Labour] resources in simulation. All [LabourRequirement] will be ignored.", MessageType.Warning);
            else if (lab.Children.Count <= 0)
                Summary.WriteMessage(this, "No [r=LabourResourceTypes] are provided in the [r=Labour] resource. All [LabourRequirement] will be ignored.", MessageType.Warning);

            // check filter groups present
            if (!FindAllChildren<LabourGroup>().Any())
            {
                string[] memberNames = new string[] { "Labour filter group" };
                results.Add(new ValidationResult($"No [f=LabourFilterGroup] is provided with the [LabourRequirement] for [a={NameWithParent}].{Environment.NewLine}Add a [LabourFilterGroup] to specify individuals for this activity.", memberNames));
            }

            // check for individual nesting.
            foreach (LabourGroup fg in this.FindAllChildren<LabourGroup>())
            {
                LabourGroup currentfg = fg;
                while (currentfg != null && currentfg.FindAllChildren<LabourGroup>().Any())
                {
                    if (currentfg.FindAllChildren<LabourGroup>().Count() > 1)
                    {
                        string[] memberNames = new string[] { "Labour requirement" };
                        results.Add(new ValidationResult(String.Format("Invalid nested labour filter groups in [f={0}] for [a={1}]. Only one nested filter group is permitted each branch. Additional filtering will be ignored.", currentfg.Name, this.Name), memberNames));
                    }
                    currentfg = currentfg.FindAllChildren<LabourGroup>().FirstOrDefault();
                }
            }

            return results;
        }
        #endregion

        #region descriptive summary

        /// <inheritdoc/>
        public override List<(IEnumerable<IModel> models, bool include, string borderClass, string introText, string missingText)> GetChildrenInSummary()
        {
            return new List<(IEnumerable<IModel> models, bool include, string borderClass, string introText, string missingText)>
            {
                (FindAllChildren<LabourGroup>(), true, "childgroupfilterborder", "The required labour will be taken from the following groups:", "No LabourGroups provided to define labour")
            };
        }

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.Write($"\r\n<div class=\"activityentry\">");
                htmlWriter.Write($"{CLEMModel.DisplaySummaryValueSnippet(LabourPerUnit, "Rate not set")} day{((LabourPerUnit==1)?"":"s")} is required ");

                if ((Measure??"").ToLower() != "fixed")
                {
                    if (WholeUnitBlocks)
                    {
                        if (UnitSize == 1)
                            htmlWriter.Write($"for each ");
                        else
                        {
                            htmlWriter.Write($"as whole units of ");
                            htmlWriter.Write($"{CLEMModel.DisplaySummaryValueSnippet(UnitSize, "Unit not set")} ");
                        }
                    }

                    htmlWriter.Write($"per {CLEMModel.DisplaySummaryValueSnippet(Measure, "Measure not set")}");
                    if(ApplyToAll)
                        htmlWriter.Write($" applied to each person specified");
                }
                htmlWriter.Write($".</div>");


                htmlWriter.Write($"\r\n<div class=\"activityentry\">Labour will be limited ");
                switch (LimitStyle)
                {
                    case LabourLimitType.AsDaysRequired:
                        htmlWriter.Write($"as the days required</div>");
                        break;
                    case LabourLimitType.AsTotalAllowed:
                        htmlWriter.Write($"as the total days permitted in the month</div>");
                        break;
                    case LabourLimitType.ProportionOfDaysRequired:
                        htmlWriter.Write($"as a proportion of the days required and therefore total required</div>");
                        break;
                    default:
                        break;
                }

                if (MaximumPerGroup > 0)
                    htmlWriter.Write($"\r\n<div class=\"activityentry\">Labour will be supplied for each filter group up to <span class=\"setvalue\">{MaximumPerGroup}</span> day{((MaximumPerGroup == 1) ? "" : "s")}</div>");

                if (MinimumPerPerson > 0)
                    htmlWriter.Write($"\r\n<div class=\"activityentry\">Labour will not be supplied if less than <span class=\"setvalue\">{MinimumPerPerson}</span> day{((MinimumPerPerson == 1) ? "" : "s")} is required</div>");

                if (MaximumPerPerson > 0 && MaximumPerPerson < 31)
                    htmlWriter.Write($"\r\n<div class=\"activityentry\">No individual can provide more than <span class=\"setvalue\">{MaximumPerPerson}</span> days</div>");

                if (ApplyToAll)
                    htmlWriter.Write("\r\n<div class=\"activityentry\">All people matching the below criteria (first level) will perform this task. (e.g. all children)</div>");

                return htmlWriter.ToString(); 
            }
        }

        ///// <inheritdoc/>
        //public override string ModelSummaryInnerOpeningTags()
        //{
        //    using (StringWriter htmlWriter = new StringWriter())
        //    {
        //        //htmlWriter.Write("\r\n<div class=\"labourgroupsborder\">");
        //        htmlWriter.Write("<div class=\"labournote\">The required labour will be taken from each of the following groups</div>");

        //        if (this.FindAllChildren<LabourGroup>().Count() == 0)
        //        {
        //            htmlWriter.Write("\r\n<div class=\"filterborder clearfix\">");
        //            htmlWriter.Write("<div class=\"filtererror\">No filter group provided</div>");
        //            htmlWriter.Write("</div>");
        //        }
        //        return htmlWriter.ToString(); 
        //    }
        //} 
        #endregion


    }
}
