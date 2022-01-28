using System;
using System.Collections.Generic;
using System.Text;
using CustomGuildChallenges.API;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using xTile.Dimensions;
using Object = StardewValley.Object;
using Rectangle = xTile.Dimensions.Rectangle;

namespace CustomGuildChallenges
{
  /// <summary>
  ///   Custom implementation of the adventure guild
  ///   Required in order to update the slayer list and rewards
  /// </summary>
  public class CustomAdventureGuild : AdventureGuild
  {
    public const string StandardMapPath = "Maps\\AdventureGuild";
    public const string StandardMapName = "AdventureGuild";

    protected readonly NPC Gil = new(null, new Vector2(-1000f, -1000f), "AdventureGuild", 2, "Gil", false, null,
      Game1.content.Load<Texture2D>("Portraits\\Gil"));

    protected bool talkedToGil;

    // Required to reimplement Monster Kill List and Gil's rewards
    public override bool checkAction(Location tileLocation, Rectangle viewport, Farmer who)
    {
      switch (map.GetLayer("Buildings").Tiles[tileLocation] != null
        ? map.GetLayer("Buildings").Tiles[tileLocation].TileIndex
        : -1)
      {
        case 1306:
          ShowNewMonsterKillList();
          CustomGuildChallengeMod.Instance.Monitor.Log("Showing custom kill list for CustomGuildChallenges");
          return true;
        case 1291:
        case 1292:
        case 1355:
        case 1356:
        case 1357:
        case 1358:
          TalkToGil();
          return true;
        default:
          return base.checkAction(tileLocation, viewport, who);
      }
    }


    /// <summary>
    ///   Creates the reward item using StardewValley.Objects.ObjectFactory
    /// </summary>
    /// <param name="rewardType"></param>
    /// <param name="rewardItemNumber"></param>
    /// <returns></returns>
    public virtual Item CreateReward(int rewardType, int rewardItemNumber, int rewardItemStack)
    {
      CustomGuildChallengeMod.Instance.Monitor.Log("Recreating rewards");
      switch (rewardType)
      {
        case (int)ItemType.Hat:
          return new Hat(rewardItemNumber);
        case (int)ItemType.Ring:
          return new Ring(rewardItemNumber);
        case (int)ItemType.SpecialItem:
          return new SpecialItem(rewardItemNumber);
        case (int)ItemType.Boots:
          return new StardewValley.Objects.Boots(rewardItemNumber);
        default:
          return ObjectFactory.getItemFromDescription((byte)rewardType, rewardItemNumber, rewardItemStack);
      }
    }

    // Required to reset talkedToGil flag
    protected override void resetLocalState()
    {
      base.resetLocalState();
      talkedToGil = false;
    }

    protected override void resetSharedState()
    {
      if (Game1.IsMultiplayer)
        base.resetSharedState();

      else return;

      //Debug.WriteLine(characters.Count + " characters found.");
    }

    /// <summary>
    ///   Build strings for display when viewing challenge list on guild wall
    /// </summary>
    protected virtual void ShowNewMonsterKillList()
    {
      if (!Game1.player.mailReceived.Contains("checkedMonsterBoard"))
      {
        Game1.player.mailReceived.Add("checkedMonsterBoard");

        CustomGuildChallengeMod.Instance.Monitor.Log("Added monster board mail flag");
      }

      var stringBuilder = new StringBuilder();
      stringBuilder.Append(Game1.content.LoadString("Strings\\Locations:AdventureGuild_KillList_Header")
        .Replace('\n', '^') + "^");

      CustomGuildChallengeMod.Instance.Monitor.Log("Build header string");


      foreach (var challenge in ConfigChallengeHelper.ChallengeList)
      {
        var kills = 0;
        foreach (var monsterName in challenge.Info.MonsterNames)
          kills += Game1.player.stats.getMonstersKilled(monsterName);

        stringBuilder.Append(KillListLine(challenge.Info.ChallengeName, kills, challenge.Info.RequiredKillCount));
        CustomGuildChallengeMod.Instance.Monitor.Log("Updated kill count");
      }

      stringBuilder.Append(Game1.content.LoadString("Strings\\Locations:AdventureGuild_KillList_Footer")
        .Replace('\n', '^'));
      CustomGuildChallengeMod.Instance.Monitor.Log("Replace footer");
      Game1.drawLetterMessage(stringBuilder.ToString());
    }

    /// <summary>
    ///   Checks to see if there are new rewards. If not, display a dialogue from Gil
    /// </summary>
    protected virtual void TalkToGil()
    {
      var specialItemsCollected = 0;
      var rewards = new List<Item>();
      var completedChallenges = new List<SlayerChallenge>();

      // Check for available rewards
      foreach (var challenge in ConfigChallengeHelper.ChallengeList)
      {
        if (challenge.CollectedReward) continue;

        var kills = 0;
        foreach (var monsterName in challenge.Info.MonsterNames)
        {
          kills += Game1.player.stats.getMonstersKilled(monsterName);
          CustomGuildChallengeMod.Instance.Monitor.Log("Updated stats");
        }

        if (kills >= challenge.Info.RequiredKillCount)
        {
          var rewardItem = CreateReward(challenge.Info.RewardType, challenge.Info.RewardItemNumber,
            challenge.Info.RewardItemStack);
          CustomGuildChallengeMod.Instance.Monitor.Log("Creating rewards");

          if (rewardItem == null)
            throw new Exception("Invalid reward parameters for challenge " + challenge.Info.ChallengeName + ":\n" +
                                "Reward Type: " + challenge.Info.RewardType + "\n" +
                                "Reward Item Number: " + challenge.Info.RewardItemNumber + "\n");
          if (challenge.Info.RewardType == 0 && challenge.Info.RewardItemNumber == 434) // Stardrop award
          {
            Game1.drawDialogue(Gil, ConfigChallengeHelper.Config.GilSpecialGiftDialogue);

            Game1.player.holdUpItemThenMessage(rewardItem);
            specialItemsCollected++;

            challenge.CollectedReward = true;
            CustomGuildChallengeMod.Instance.Monitor.Log("You got a stardrop, congratulations!", LogLevel.Info);

            break;
          }
          // Add special section for special item

          if (rewardItem is SpecialItem specialItem)
          {
            Game1.drawDialogue(Gil, ConfigChallengeHelper.Config.GilSpecialGiftDialogue);

            Game1.player.holdUpItemThenMessage(specialItem);
            specialItem.actionWhenReceived(Game1.player);

            specialItemsCollected++;
            challenge.CollectedReward = true;
            CustomGuildChallengeMod.Instance.Monitor.Log("Special item awarded");

            break;
          }

          completedChallenges.Add(challenge);
          CustomGuildChallengeMod.Instance.Monitor.Log("Challenge complete!");
        }
      }

      // Display rewards/dialogue for talking to Gil
      if (specialItemsCollected > 0)
        return;
      if (completedChallenges.Count > 0)
      {
        Item rewardItem;
        foreach (var challenge in completedChallenges)
        {
          rewardItem = CreateReward(challenge.Info.RewardType, challenge.Info.RewardItemNumber,
            challenge.Info.RewardItemStack);
          if (rewardItem is Object)
          {
            rewardItem.specialItem = true;
            rewards.Add(rewardItem);
          }
          else if (!Game1.player.hasOrWillReceiveMail("Gil_" + challenge.Info.ChallengeName + "_" + rewardItem.Name))
          {
            Game1.player.mailReceived.Add("Gil_" + challenge.Info.ChallengeName + "_" + rewardItem.Name);
            rewards.Add(rewardItem);
            CustomGuildChallengeMod.Instance.Monitor.Log("Adding mail flag");
          }

          challenge.CollectedReward = true;
        }

        Game1.activeClickableMenu = new ItemGrabMenu(rewards);
      }
      else if (talkedToGil)
        Game1.drawDialogue(Gil, ConfigChallengeHelper.Config.GilSleepingDialogue);
      else
      {
        Game1.drawDialogue(Gil, ConfigChallengeHelper.Config.GilNoRewardDialogue);
        talkedToGil = true;
      }
    }

    /// <summary>
    ///   Generates a single challenge line for the challenge board
    ///   Localization done in the config files
    /// </summary>
    /// <param name="challengeName"></param>
    /// <param name="killCount"></param>
    /// <param name="target"></param>
    /// <returns></returns>
    protected virtual string KillListLine(string challengeName, int killCount, int target)
    {
      CustomGuildChallengeMod.Instance.Monitor.Log("Creating kill list");
      if (killCount == 0)
        return "0/" + target + " ????\n\n^";
      if (killCount >= target)
        return killCount + " " + challengeName + " * \n\n ^";
      return killCount + "/" + target + " " + challengeName + " \n\n ^";
    }

    #region Constructors

    public CustomAdventureGuild() : base(StandardMapPath, StandardMapName)
    {
    }

    /// <summary>
    ///   Loads custom slayer challenge list with custom map path and name
    /// </summary>
    /// <param name="map"></param>
    /// <param name="name"></param>
    /// <param name="customChallengeList"></param>
    public CustomAdventureGuild(string map, string name) : base(map, name)
    {
    }

    internal bool HasMarlon()
    {
      foreach (var character in characters)
        if (character.Name == "Marlon")
          return true;

      return false;
    }

    internal void AddMarlon() => addCharacter(new NPC(new AnimatedSprite("Characters\\Marlon", 0, 16, 32),
      new Vector2(320f, 704f), "AdventureGuild", 2, "Marlon", false, null,
      Game1.content.Load<Texture2D>("Portraits\\Marlon")));

    #endregion
  }
}