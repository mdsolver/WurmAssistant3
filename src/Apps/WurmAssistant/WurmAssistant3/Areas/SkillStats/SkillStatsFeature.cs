﻿using System;
using System.Drawing;
using System.Threading.Tasks;
using AldursLab.WurmApi;
using AldursLab.WurmAssistant3.Areas.Features;
using AldursLab.WurmAssistant3.Areas.Insights;
using AldursLab.WurmAssistant3.Areas.Logging;
using AldursLab.WurmAssistant3.Properties;

namespace AldursLab.WurmAssistant3.Areas.SkillStats
{
    [KernelBind(BindingHint.Singleton)]
    public class SkillStatsFeature : IFeature
    {
        readonly IWurmApi wurmApi;
        readonly ILogger logger;
        readonly SkillStatsFeatureForm form;

        public SkillStatsFeature(IWurmApi wurmApi, ILogger logger, ITelemetry telemetry)
        {
            if (wurmApi == null) throw new ArgumentNullException("wurmApi");
            if (logger == null) throw new ArgumentNullException("logger");
            this.wurmApi = wurmApi;
            this.logger = logger;
            form = new SkillStatsFeatureForm(this, wurmApi, logger, telemetry);
        }

        #region IFeature

        void IFeature.Show()
        {
            form.ShowAndBringToFront();
        }

        void IFeature.Hide()
        {
        }

        string IFeature.Name
        {
            get { return "Skill Stats"; }
        }

        Image IFeature.Icon
        {
            get { return Resources.Improvement; }
        }

        async Task IFeature.InitAsync()
        {
            await Task.FromResult(true);
        }

        #endregion
    }
}
