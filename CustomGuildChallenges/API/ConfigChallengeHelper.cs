﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;

namespace CustomGuildChallenges.API
{
  public sealed class ConfigChallengeHelper : ICustomChallenges
  {
    /// <summary>
    ///   Mod configuration
    /// </summary>
    internal static ModConfig Config;

    private static IList<SlayerChallenge> _customChallengeList;
    private static IList<SlayerChallenge> _vanillaChallengeList;
    private static bool _useCustom;
    private readonly string _bugLocationName = "BugLand";

    // Location Names to find dying monsters
    private readonly string _farmLocationName = "Farm";

    /// <summary>
    ///   SMAPI API - used for saving and loading JSON files
    /// </summary>
    private readonly IModHelper Helper;

    /// <summary>
    ///   SMAPI Log Utility
    /// </summary>
    private readonly IMonitor Monitor;

    private AdventureGuild _adventureGuild;
    private CustomAdventureGuild _customAdventureGuild;

    /// <summary>
    ///   Creates guild and sets up events
    /// </summary>
    /// <param name="guild"></param>
    public ConfigChallengeHelper(IModHelper helper, ModConfig config, IEnumerable<ChallengeInfo> vanillaChallenges,
      IMonitor monitor)
    {
      Helper = helper;
      Monitor = monitor;
      Config = config;

      _useCustom = Config.CustomChallengesEnabled;

      _customChallengeList = new List<SlayerChallenge>();
      foreach (var info in Config.Challenges)
        _customChallengeList.Add(new SlayerChallenge { Info = info });

      _vanillaChallengeList = new List<SlayerChallenge>();
      foreach (var info in vanillaChallenges)
        _vanillaChallengeList.Add(new SlayerChallenge { Info = info });

      helper.Events.GameLoop.SaveCreated += SetupMonsterKilledEvent;
      helper.Events.GameLoop.SaveLoaded += SetupMonsterKilledEvent;

      helper.Events.GameLoop.Saving += PresaveData;
      helper.Events.GameLoop.Saved += InjectGuild;
      helper.Events.GameLoop.SaveLoaded += InjectGuild;
      helper.Events.GameLoop.SaveCreated += InjectGuild;

      MonsterKilled += Events_MonsterKilled;
    }

    /// <summary>
    ///   Configuration and challenge list
    /// </summary>
    internal static IList<SlayerChallenge> ChallengeList => _useCustom ? _customChallengeList : _vanillaChallengeList;

    /// <summary>
    ///   Mod's version of the Adventure Guild
    /// </summary>
    internal CustomAdventureGuild CustomAdventureGuild
    {
      get
      {
        InitLocations();
        return _customAdventureGuild;
      }
    }

    /// <summary>
    ///   Vanilla version of the Adventure Guild
    /// </summary>
    private AdventureGuild AdventureGuild
    {
      get
      {
        InitLocations();
        return _adventureGuild;
      }
    }

    /// <summary>
    ///   Is invoked each time a monster is killed
    /// </summary>
    public event EventHandler<Monster> MonsterKilled;

    /// <summary>
    ///   Add a challenge for the player to complete. The global config will not be updated.
    /// </summary>
    /// <param name="challengeName"></param>
    /// <param name="killCount"></param>
    /// <param name="rewardItemType"></param>
    /// <param name="rewardItemNumber"></param>
    /// <param name="monsterNames"></param>
    public void AddChallenge(string challengeName, int killCount, int rewardItemType, int rewardItemNumber,
      int rewardItemStack, IList<string> monsterNames)
    {
      var challenge = new SlayerChallenge
      {
        CollectedReward = false,
        Info = new ChallengeInfo
        {
          ChallengeName = challengeName,
          RequiredKillCount = killCount,
          RewardType = rewardItemType,
          RewardItemNumber = rewardItemNumber,
          RewardItemStack = rewardItemStack,
          MonsterNames = monsterNames.ToList()
        }
      };

      ChallengeList.Add(challenge);
    }

    /// <summary>
    ///   Remove a challenge from the challenge list. The global config will not be updated.
    /// </summary>
    /// <param name="challengeName"></param>
    public void RemoveChallenge(string challengeName)
    {
      for (var i = 0; i < ChallengeList.Count; i++)
        if (ChallengeList[i].Info.ChallengeName == challengeName)
        {
          ChallengeList.RemoveAt(i);
          break;
        }
    }

    /// <summary>
    ///   Set the dialogue for Gil
    /// </summary>
    /// <param name="initialNoRewardDialogue"></param>
    /// <param name="secondNoRewardDialogue"></param>
    /// <param name="specialRewardDialogue"></param>
    public void SetGilDialogue(string initialNoRewardDialogue, string secondNoRewardDialogue,
      string specialRewardDialogue)
    {
      Config.GilNoRewardDialogue = initialNoRewardDialogue;
      Config.GilSleepingDialogue = secondNoRewardDialogue;
      Config.GilSpecialGiftDialogue = specialRewardDialogue;
    }

    private void InitLocations()
    {
      Console.WriteLine("Initializing locations for custom guild.");
      if (Game1.locations == null)
        throw new InvalidOperationException("Can't access Adventure Guild before the game is initialised.");

      if (_adventureGuild == null)
      {
        _adventureGuild =
          new AdventureGuild(CustomAdventureGuild.StandardMapPath, CustomAdventureGuild.StandardMapName);
        _customAdventureGuild = new CustomAdventureGuild();
      }
    }

    /// <summary>
    ///   Setup event that detects whether monsters are killed
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SetupMonsterKilledEvent(object sender, EventArgs e)
    {
      // Inject into all mines
      Helper.Events.Player.Warped -= Player_Warped;
      Helper.Events.Player.Warped += Player_Warped;
    }

    /// <summary>
    ///   Sets up detection for when a monster dies in the mines
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Player_Warped(object sender, WarpedEventArgs e)
    {
      e.NewLocation.characters.OnValueRemoved -= Characters_OnValueRemoved;
      e.NewLocation.characters.OnValueRemoved += Characters_OnValueRemoved;
    }

    /// <summary>
    ///   Fires the MonsterKilled event if the removed NPC is a monster and has 0 or less health
    ///   or its a grub that doesn't have a fixed health value of -500 for when it transforms into a fly
    /// </summary>
    /// <param name="value"></param>
    private void Characters_OnValueRemoved(NPC value)
    {
      // Grub at -500 health means it transformed
      // This is a hacky way to detect transformation, but the alternative is reflection
      if (value is Monster monster)
      {
        if (monster.Health <= 0
            || value is Grub grub && grub.Health != -500
            || monster.Name == Monsters.Mummy && monster.Health == monster.MaxHealth)
          MonsterKilled?.Invoke(Game1.currentLocation, monster);

        Monitor.Log("Monster killed!");
      }
    }

    /// <summary>
    ///   Saves the status of challenges and switches the
    ///   CustomAdventureGuild with AdventureGuild to prevent
    ///   crashing the save process
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void PresaveData(object sender, EventArgs e)
    {
      var saveDataPath = Path.Combine("saveData", Constants.SaveFolderName + ".json");
      var saveData = new SaveData();

      foreach (var slayerChallenge in ChallengeList)
      {
        var save = new ChallengeSave
        {
          ChallengeName = slayerChallenge.Info.ChallengeName,
          Collected = slayerChallenge.CollectedReward
        };

        saveData.Challenges.Add(save);
      }

      Helper.Data.WriteJsonFile(saveDataPath, saveData);

      Monitor.Log("Writing to savedata");

      // Remove custom location and add back the original location
      Game1.locations.Remove(CustomAdventureGuild);
      Game1._locationLookup.Remove(CustomAdventureGuild.Name);
      Game1.locations.Add(AdventureGuild);
    }

    /// <summary>
    ///   Read the save data file and replace the AdventureGuild with
    ///   CustomAdventureGuild
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void InjectGuild(object sender, EventArgs e)
    {
      var saveDataPath = Path.Combine("saveData", Constants.SaveFolderName + ".json");
      var saveData = Helper.Data.ReadJsonFile<SaveData>(saveDataPath) ?? new SaveData();

      foreach (var savedChallenge in saveData.Challenges)
      foreach (var slayerChallenge in ChallengeList)
        if (savedChallenge.ChallengeName == slayerChallenge.Info.ChallengeName)
        {
          slayerChallenge.CollectedReward = savedChallenge.Collected;
          break;
        }

      if (Game1.player.IsMainPlayer && !CustomAdventureGuild.HasMarlon()) CustomAdventureGuild.AddMarlon();

      // Kill old guild, replace with new guild
      Game1.locations.Remove(AdventureGuild);
      Game1._locationLookup.Remove(AdventureGuild.Name);
      Game1.locations.Add(CustomAdventureGuild);

      Monitor.Log("Adding custom Adventure Guild");
    }

    /// <summary>
    ///   Adds kills for monsters on the farm (if enabled) and Wilderness Golems
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Events_MonsterKilled(object sender, Monster e)
    {
      if (sender is not GameLocation location) return;
      if (Game1.player.currentLocation.Name != location.Name) return;

      var monsterName = e.Name;

      // The game does not reward kills on the farm
      if (location.Name == _farmLocationName && (Config.CountKillsOnFarm || monsterName == Monsters.WildernessGolem))
      {
        Game1.player.stats.monsterKilled(monsterName);

        Monitor.Log("Farm monster killed");
      }
      // The game does not differentiate between bugs and mutant bugs
      else if (location.Name == _bugLocationName)
      {
        var mutantName = "Mutant " + monsterName;
        Game1.player.stats.monsterKilled(mutantName);
        Game1.player.stats.specificMonstersKilled[monsterName]--;
        monsterName = mutantName;
      }
      // The game does not give mummy kills to farmhands
      else if (e.Name == Monsters.Mummy && Game1.IsClient) Game1.player.stats.monsterKilled(Monsters.Mummy);
      // else do nothing - game already handles the monster kill

      if (Config.DebugMonsterKills)
        Monitor.Log(monsterName + " killed for total of " + Game1.player.stats.getMonstersKilled(monsterName),
          LogLevel.Debug);

      NotifyIfChallengeComplete(monsterName);
    }


    /// <summary>
    ///   Display message to see Gil if the challenge just completed
    /// </summary>
    private void NotifyIfChallengeComplete(string monsterKilled)
    {
      foreach (var challenge in ChallengeList)
      {
        if (challenge.CollectedReward) continue;

        var kills = 0;
        var hasMonster = false;

        foreach (var monsterName in challenge.Info.MonsterNames)
        {
          kills += Game1.player.stats.getMonstersKilled(monsterName);
          if (monsterName == monsterKilled) hasMonster = true;
        }

        if (hasMonster && kills == challenge.Info.RequiredKillCount)
        {
          var message = Game1.content.LoadString("Strings\\StringsFromCSFiles:Stats.cs.5129");
          if (!IsVanillaChallenge(challenge.Info)) Game1.showGlobalMessage(message);
          break;
        }

        Monitor.Log("Gil message sent!");
      }
    }

    /// <summary>
    ///   Check to see if challenge is vanilla
    /// </summary>
    /// <param name="info"></param>
    /// <returns></returns>
    private bool IsVanillaChallenge(ChallengeInfo info)
    {
      foreach (var challenge in CustomGuildChallengeMod.VanillaChallenges)
      {
        if (challenge.RequiredKillCount == info.RequiredKillCount && challenge.ChallengeName == info.ChallengeName
                                                                  && challenge.MonsterNames.All(x =>
                                                                    info.MonsterNames.Any(y => x == y)) &&
                                                                  info.MonsterNames.All(x =>
                                                                    challenge.MonsterNames.Any(y => y == x)))
          return true;

        Monitor.Log("Vanilla Challenges Detected");
      }

      return false;
    }
  }
}