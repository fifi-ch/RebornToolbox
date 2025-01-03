﻿using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.Reflection;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace RebornToolbox.Features.ChocoboRacing;

public class ChocoboRacing
{
    public bool IsRunning { get; set; } = false;

    public IEnumerable<ContentRoulette> ContentRoulettes;

    public ChocoboRacing()
    {
        ContentRoulettes = Svc.Data.GetExcelSheet<ContentRoulette>(Svc.ClientState.ClientLanguage)!;
        Svc.Framework.Update += OnUpdate;
    }

    private unsafe bool ContentsFinderConfirm()
    {
        if (GenericHelpers.TryGetAddonByName("ContentsFinderConfirm", out AtkUnitBase* addonContentsFinderConfirm) &&
            GenericHelpers.IsAddonReady(addonContentsFinderConfirm))
        {
            Svc.Log.Debug("Queue Helper - Confirming DutyPop");
            Callback.Fire(addonContentsFinderConfirm, true, 8);
            return true;
        }

        return false;
    }

    public bool IsMoving
    {
        get { return Svc.KeyState[Plugin.Configuration.ChocoboRacingConfig.MoveForwardKey]; }
        set
        {
            if (Svc.KeyState[Plugin.Configuration.ChocoboRacingConfig.MoveForwardKey] != value)
            {
                if (value) DalamudReflector.SetKeyState(Plugin.Configuration.ChocoboRacingConfig.MoveForwardKey, 1);
                else DalamudReflector.SetKeyState(Plugin.Configuration.ChocoboRacingConfig.MoveForwardKey, 0);
            }
        }
    }

    public unsafe byte ChocoboLevel
    {
        get
        {
            var manager = RaceChocoboManager.Instance();
            if (manager == null) return 0;
            var rank = manager->Rank;
            return rank;
        }
    }

    private unsafe void OnUpdate(IFramework framework)
    {
        if (!Plugin.Configuration.ChocoboRacingConfig.Enabled)
        {
            if (IsRunning)
                IsRunning = false;
            return;
        }

        if (!IsRunning) return;
        if (!GenericHelpers.IsScreenReady())
        {
            IsMoving = false;
            return;
        }

        if (Plugin.Configuration.ChocoboRacingConfig.StopAtMaxRank && ChocoboLevel == 40)
        {
            if (IsMoving)
                IsMoving = false;
            IsRunning = false;
            return;
        }

        if (Plugin.Configuration.ChocoboRacingConfig.AutoRun && Svc.Condition[ConditionFlag.ChocoboRacing] &&
            GenericHelpers.TryGetAddonByName("_RaceChocoboParameter", out AtkUnitBase* raceChocoboParameter))
        {
            Svc.Log.Verbose("Zoom zoom....");
            var lathered = raceChocoboParameter->GetImageNodeById(3)->IsVisible();
            var stamina = raceChocoboParameter->GetNodeById(5)->GetAsAtkCounterNode()->NodeText.ToString();
            var hasStamina = !string.Equals(stamina, "0.00%");
            if (!lathered && hasStamina && !Plugin.Configuration.ChocoboRacingConfig.AlwaysRun)
                IsMoving = true;
            else if (Plugin.Configuration.ChocoboRacingConfig.AlwaysRun)
                IsMoving = true;
            else
                IsMoving = false;
            return;
        }

        if (IsMoving)
            IsMoving = false;
        if (EzThrottler.Throttle("FeatherToTheMetal", 1500))
        {
            if (Svc.Condition[ConditionFlag.BoundToDuty97])
            {
                ContentsFinderConfirm();
                return;
            }

            var cfAgent = AgentContentsFinder.Instance();
            if (!GenericHelpers.TryGetAddonByName("ContentsFinder", out AddonContentsFinder* addon))
            {
                cfAgent->OpenRouletteDuty((byte)Plugin.Configuration.ChocoboRacingConfig.RaceRoute);

                return;
            }

            var selectedContent = cfAgent->SelectedContent;
            if (!selectedContent.Any(c =>
                    c.Id == (byte)Plugin.Configuration.ChocoboRacingConfig.RaceRoute &&
                    c.ContentType == ContentsId.ContentsType.Roulette))
            {
                SelectDuty(addon);
                return;
            }

            var selectedDutyName = addon->AtkValues[18].GetValueAsString();
            if (selectedDutyName.Contains(GetSelectedDutyName(), StringComparison.InvariantCultureIgnoreCase))
            {
                Callback.Fire((AtkUnitBase*)addon, true, 12, 0);
            }
        }
    }

    private string GetSelectedDutyName()
    {
        var routeId = (byte)Plugin.Configuration.ChocoboRacingConfig.RaceRoute;
        var row = ContentRoulettes.First(c => c.RowId == routeId);
        return row.Name.ToString();
    }

    // Credit: https://github.com/ffxivcode/AutoDuty/blob/26a61eefdba148bc5f46694f915f402315e9f128/AutoDuty/Helpers/QueueHelper.cs#L247
    private uint HeadersCount(uint before, List<AtkComponentTreeListItem> list)
    {
        uint count = 0;
        for (int i = 0; i < before; i++)
        {
            if (list[i].UIntValues[0] == 4 || list[i].UIntValues[0] == 2)
                count++;
        }

        return count;
    }

// Credit: https://github.com/ffxivcode/AutoDuty/blob/26a61eefdba148bc5f46694f915f402315e9f128/AutoDuty/Helpers/QueueHelper.cs#L258
    private unsafe void SelectDuty(AddonContentsFinder* addonContentsFinder)
    {
        if (addonContentsFinder == null) return;

        var vectorDutyListItems = addonContentsFinder->DutyList->Items;
        List<AtkComponentTreeListItem> listAtkComponentTreeListItems = [];
        vectorDutyListItems.ForEach(pointAtkComponentTreeListItem => listAtkComponentTreeListItems.Add(*(pointAtkComponentTreeListItem.Value)));
        Callback.Fire((AtkUnitBase*)addonContentsFinder, true, 3, HeadersCount((uint)addonContentsFinder->DutyList->SelectedItemIndex, listAtkComponentTreeListItems) + 1); // - (HeadersCount(addonContentsFinder->DutyList->SelectedItemIndex, listAtkComponentTreeListItems) + 1));
    }
}