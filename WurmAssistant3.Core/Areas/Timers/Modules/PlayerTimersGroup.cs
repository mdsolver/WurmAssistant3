﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AldursLab.Essentials.Extensions.DotNet;
using AldursLab.PersistentObjects;
using AldursLab.WurmApi;
using AldursLab.WurmAssistant3.Core.Areas.Logging.Contracts;
using AldursLab.WurmAssistant3.Core.Areas.SoundEngine.Contracts;
using AldursLab.WurmAssistant3.Core.Areas.Timers.Contracts;
using AldursLab.WurmAssistant3.Core.Areas.Timers.Modules.Timers;
using AldursLab.WurmAssistant3.Core.Areas.Timers.Modules.Timers.Custom;
using AldursLab.WurmAssistant3.Core.Areas.Timers.Views;
using AldursLab.WurmAssistant3.Core.Areas.Timers.Views.Timers;
using AldursLab.WurmAssistant3.Core.Areas.TrayPopups.Contracts;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Ninject;

namespace AldursLab.WurmAssistant3.Core.Areas.Timers.Modules
{
    [PersistentObject("TimersFeature_PlayerTimersGroup")]
    public class PlayerTimersGroup : PersistentObjectBase, IInitializable
    {
        // note: player name is the persistent object id

        [JsonProperty]
        HashSet<TimerDefinition> activeTimerDefinitions = new HashSet<TimerDefinition>();
        [JsonProperty] //saved to figure, on which servers is current character, in each group
        Dictionary<string, string> groupIdToServerMap = new Dictionary<string, string>();
        [JsonProperty] //saved to remember last group this char was on
        string currentServerGroupId = ServerGroup.UnknownId;
        [JsonProperty] //saved to make init searches quicker
        DateTime lastServerGroupCheckup = DateTime.MinValue;
        [JsonProperty] //last server this char was on
        string currentServerName = null;

        readonly List<WurmTimer> wurmTimers = new List<WurmTimer>();

        public string CharacterName { get; private set; }

        readonly PlayerLayoutView layoutControl;
        readonly TimersFeature timersFeature;
        readonly IWurmApi wurmApi;
        readonly ILogger logger;
        readonly TimerDefinitions timerDefinitions;

        bool currentServerGroupFound;

        public TimersFeature TimersFeature { get { return timersFeature; } }

        public PlayerTimersGroup(TimersFeature timersFeature, string characterName, [NotNull] IWurmApi wurmApi,
            [NotNull] ILogger logger, [NotNull] ISoundEngine soundEngine, [NotNull] ITrayPopups trayPopups,
            [NotNull] TimerDefinitions timerDefinitions)
            : base(characterName) 
        {
            if (characterName == null) throw new ArgumentNullException("characterName");
            if (wurmApi == null) throw new ArgumentNullException("wurmApi");
            if (logger == null) throw new ArgumentNullException("logger");
            if (soundEngine == null) throw new ArgumentNullException("soundEngine");
            if (trayPopups == null) throw new ArgumentNullException("trayPopups");
            if (timerDefinitions == null) throw new ArgumentNullException("timerDefinitions");
            this.CharacterName = characterName;
            this.timersFeature = timersFeature;
            this.wurmApi = wurmApi;
            this.logger = logger;
            this.timerDefinitions = timerDefinitions;

            if (activeTimerDefinitions == null)
                activeTimerDefinitions = new HashSet<TimerDefinition>();
            if (groupIdToServerMap == null)
                groupIdToServerMap = new Dictionary<string, string>();

            layoutControl = new PlayerLayoutView(this);
            this.timersFeature.RegisterTimersGroup(layoutControl);
            wurmApi.LogsMonitor.Subscribe(CharacterName, LogType.AllLogs, OnNewLogEvents);
        }

        public HashSet<TimerDefinition> ActiveTimerDefinitions
        {
            get { return activeTimerDefinitions; }
            set { activeTimerDefinitions = value; FlagAsChanged(); }
        }

        public Dictionary<string, string> GroupIdToServerMap
        {
            get { return groupIdToServerMap; }
            set { groupIdToServerMap = value; FlagAsChanged(); }
        }

        public string CurrentServerGroupId
        {
            get { return currentServerGroupId; }
            set { currentServerGroupId = value; FlagAsChanged(); }
        }

        public DateTime LastServerGroupCheckup
        {
            get { return lastServerGroupCheckup; }
            private set { lastServerGroupCheckup = value; FlagAsChanged(); }
        }

        public string CurrentServerName
        {
            get { return currentServerName; }
            private set { currentServerName = value; FlagAsChanged(); }
        }

        public void Initialize()
        {
            PerformAsyncInits(LastServerGroupCheckup);
        }

        private async void PerformAsyncInits(DateTime lastCheckup)
        {
            try
            {
                TimeSpan timeToCheck = (DateTime.Now - lastCheckup).ConstrainToRange(TimeSpan.FromDays(7), TimeSpan.FromDays(120));

                var lgs = await wurmApi.LogsHistory.ScanAsync(new LogSearchParameters()
                {
                    LogType = LogType.Event,
                    MinDate = DateTime.Now - timeToCheck,
                    MaxDate = DateTime.Now,
                    CharacterName = CharacterName,
                });

                string mostRecentGroup = ServerGroup.UnknownId;
                string mostRecentServerName = null;

                foreach (var line in lgs)
                {
                    if (line.Content.Contains("You are on"))
                    {
                        string serverName;
                        string group = GetServerGroupFromLine(line.Content, out serverName);
                        if (group != ServerGroup.UnknownId)
                        {
                            if (!String.IsNullOrEmpty(serverName))
                            {
                                GroupIdToServerMap[group] = serverName;
                                FlagAsChanged();
                            }
                            mostRecentServerName = serverName;
                            mostRecentGroup = group;
                            // we've got what we needed
                            break;
                        }
                    }
                }

                if (mostRecentGroup != ServerGroup.UnknownId && !currentServerGroupFound)
                {
                    CurrentServerGroupId = mostRecentGroup;
                    if (mostRecentServerName != null) CurrentServerName = mostRecentServerName;
                    currentServerGroupFound = true;
                    LastServerGroupCheckup = DateTime.Now;
                    FlagAsChanged();
                }

                InitTimers(ActiveTimerDefinitions);

                layoutControl.EnableAddingTimers();
            }
            catch (Exception _e)
            {
                logger.Error(_e, "problem updating current server group");
            }
        }

        public void Stop()
        {
            wurmApi.LogsMonitor.Unsubscribe(CharacterName, OnNewLogEvents);
            timersFeature.UnregisterTimersGroup(layoutControl);
            foreach (var timer in wurmTimers)
            {
                timer.Stop();
            }
            layoutControl.Dispose();
        }

        internal void AddNewTimer()
        {
            var availableTimers = timerDefinitions.GetDefinitionsOfUnusedTimers(ActiveTimerDefinitions);
            
            TimersChoiceForm ui = new TimersChoiceForm(availableTimers, timersFeature.GetModuleUi());
            if (ui.ShowDialogCenteredOnForm(timersFeature.GetModuleUi()) == System.Windows.Forms.DialogResult.OK)
            {
                InitTimers(ui.Result);
            }
        }

        internal void RemoveTimer(WurmTimer timer)
        {
            try
            {
                var type = timerDefinitions.FindDefinitionForTimer(timer);
                ActiveTimerDefinitions.Remove(type);
                FlagAsChanged();
            }
            catch (InvalidOperationException _e)
            {
                if (timer is CustomTimer)
                {
                    logger.Info("there was an issue with removing custom timer, attempting fix now");
                    ActiveTimerDefinitions.RemoveWhere(x => timer.TimerDefinitionId.Name == x.TimerDefinitionId.Name);
                    logger.Info("fixed");
                    FlagAsChanged();
                }
                else
                {
                    logger.Error(_e, "problem removing timer from active list, ID: " + timer.Name);
                }
            }
            timerDefinitions.Unload(timer);
            wurmTimers.Remove(timer);
            timer.Stop();
        }

        void InitTimers(HashSet<TimerDefinition> definitions)
        {
            var definitionsArray = definitions.ToArray();
            foreach (var timerDefinition in definitionsArray)
            {
                try
                {
                    WurmTimer newTimer = timerDefinitions.NewTimerFactory(timerDefinition, CharacterName);
                    newTimer.Initialize(this, CharacterName, timerDefinition);
                    wurmTimers.Add(newTimer);
                    ActiveTimerDefinitions.Add(timerDefinition);
                    FlagAsChanged();
                }
                catch (Exception exception)
                {
                    ActiveTimerDefinitions.Remove(timerDefinition);
                    logger.Error(exception, "Error at InitTimers for timer " + timerDefinition.ToDebugString());
                    FlagAsChanged();
                }
            }
        }

        public void RemoveDeletedCustomTimer(string nameId)
        {
            var timers = wurmTimers.ToArray();
            foreach (var timer in timers)
            {
                if (timer.TimerDefinitionId.Name == nameId)
                {
                    timerDefinitions.Unload(timer);
                    RemoveTimer(timer);
                }
            }
        }

        internal void RegisterNewControlTimer(TimerDisplayView timerView)
        {
            layoutControl.RegisterNewTimerDisplay(timerView);
        }

        internal void UnregisterControlTimer(TimerDisplayView timerView)
        {
            layoutControl.UnregisterTimerDisplay(timerView);
        }

        internal void Update()
        {
            foreach (var timer in wurmTimers)
            {
                if (timer.InitCompleted) timer.Update();
                else if (timer.RunUpdateRegardlessOfInitCompleted) timer.Update();
            }
        }

        /// <summary>
        /// Attempts to extract correct server group from a wurm log entry. If no group could be extracted,
        /// will return Unknown and out serverName will be null.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="serverName"></param>
        /// <returns></returns>
        string GetServerGroupFromLine(string line, out string serverName)
        {
            //[15:14:17] 75 other players are online. You are on Exodus (774 totally in Wurm).
            Match match = Regex.Match(line, @"\d+ other players are online.*\. You are on (.+) \(", RegexOptions.Compiled);
            if (match.Success)
            {
                serverName = match.Groups[1].Value;
                try
                {
                    var server = wurmApi.Servers.GetByName(new ServerName(serverName));
                    return server.ServerGroup.ServerGroupId;
                }
                catch (DataNotFoundException)
                {
                    logger.Error(string.Format("no server found in IWurmApi for name: {0}, line: {1}",
                        serverName,
                        line ?? "NULL LINE"));
                    serverName = null;
                    return ServerGroup.UnknownId;
                }
            }
            else
            {
                serverName = null;
                logger.Error("could not match server name from line: " + (line ?? "NULL"));
                return ServerGroup.UnknownId;
            }
        }

        void OnNewLogEvents(object sender, LogsMonitorEventArgs e)
        {
            //Logger.LogDebug("events received", this);
            if (e.LogType == LogType.Event)
            {
                foreach (var entry in e.WurmLogEntries)
                {
                    try
                    {
                        //detect server travel and update information
                        if (entry.Content.Contains("You are on"))
                        {
                            string serverName;
                            string group = GetServerGroupFromLine(entry.Content, out serverName);
                            if (group != ServerGroup.UnknownId)
                            {
                                if (!String.IsNullOrEmpty(serverName))
                                {
                                    GroupIdToServerMap[group] = serverName;
                                }
                                CurrentServerGroupId = group;
                                currentServerGroupFound = true;
                                LastServerGroupCheckup = DateTime.Now;
                                CurrentServerName = serverName;
                                FlagAsChanged();
                            }
                        }
                    }
                    catch (Exception _e)
                    {
                        logger.Error(_e, "problem parsing line while updating current server group, line: " + entry);
                    }
                }

                foreach (var timer in wurmTimers)
                {
                    if (timer.InitCompleted && CurrentServerGroupId == timer.TimerDefinitionId.ServerGroupId)
                    {
                        foreach (var entry in e.WurmLogEntries)
                        {
                            timer.HandleNewEventLogLine(entry);
                        }
                    }
                }
            }
            if (e.LogType == LogType.Skills)
            {
                //call all timers with wurmskill handler
                foreach (var timer in wurmTimers)
                {
                    if (timer.InitCompleted && CurrentServerGroupId == timer.TimerDefinitionId.ServerGroupId)
                    {
                        foreach (var line in e.WurmLogEntries)
                        {
                            timer.HandleNewSkillLogLine(line);
                        }
                    }
                }
            }

            foreach (var timer in wurmTimers)
            {
                if (timer.InitCompleted && CurrentServerGroupId == timer.TimerDefinitionId.ServerGroupId)
                {
                    timer.HandleAnyLogLine(e);
                }
            }
        }

        internal TimersForm GetModuleUI()
        {
            return timersFeature.GetModuleUi();
        }
    }
}