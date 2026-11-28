using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using TrashHunter;
using UnityEditor;
using UnityEngine;

if (EditorApplication.isPlayingOrWillChangePlaymode)
    throw new InvalidOperationException("Run in Edit Mode to keep the current game and its slot isolated.");

const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
const string slotVariable = "TRASH_HUNTER_EDITOR_SAVE_DIR";
var assembly = typeof(GameState).Assembly;
var lootType = assembly.GetType("TrashHunter.LootCatalog", true);
var questType = assembly.GetType("TrashHunter.QuestCatalog", true);
var loot = lootType.GetMethod("Load").Invoke(null, null);
var catalog = questType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
    new[] { File.ReadAllText("AgentScripts/Fixtures/i170-quests.json"), loot });
var root = Path.GetFullPath(Path.Combine(
    System.Environment.GetEnvironmentVariable("TRASH_HUNTER_QUEST_CHECK_ROOT") ?? "Logs/I-170",
    Guid.NewGuid().ToString("N")));
var folder = Path.Combine(root, "journal");
Directory.CreateDirectory(folder);
var slot = Path.Combine(folder, "slot.json");
var pending = Path.Combine(folder, "slot.pending");
var checks = new List<string>();
var previousFolder = Environment.GetEnvironmentVariable(slotVariable);
var legacy = File.ReadAllText("AgentScripts/Fixtures/i120-search-acceptance.json");
const string inspectionQuest = "inspect-radio";
const string deliveryQuest = "sell-and-deliver";
var wrong = new HomeItem { expedition = 1, key = "dial-a" };
var sale = new HomeItem { expedition = 1, key = "strip-a" };
var olderRadio = new HomeItem { expedition = 1, key = "radio-a" };
var radio = new HomeItem { expedition = 2, key = "radio-a" };
try
{
    var legacyFolder = Path.Combine(root, "legacy");
    Directory.CreateDirectory(legacyFolder);
    File.WriteAllText(Path.Combine(legacyFolder, "slot.json"), legacy);
    using (var state = Load(legacyFolder))
    {
        Check(state.Location == RunLocation.Search && state.Expedition == 1 &&
            state.TryGetPose("radio-a", out _) && !state.HasBag && state.Balance == 0 &&
            state.Quests.IsAvailable(inspectionQuest) && !state.Quests.IsAvailable(deliveryQuest),
            "Format 1 restores the search with an empty journal and no first sale");
    }

    var seed = Payload(legacy);
    seed["format"] = 2;
    seed["location"] = (int)RunLocation.Trader;
    seed["expedition"] = 2;
    seed["hasBag"] = true;
    seed["world"] = new JArray();
    seed["home"] = JArray.FromObject(new[] { wrong, sale, olderRadio });
    WriteSeed(slot, seed);
    using (var state = Load(folder))
    {
        Check(state.Location == RunLocation.Trader && state.Home.Count == 3 && state.Balance == 0,
            "Format 2 restores location, stock and cash");
        Check(!state.Quests.Accept(deliveryQuest) && state.Quests.Accept(inspectionQuest) &&
            !state.Quests.Accept(inspectionQuest) && AtStep(state, inspectionQuest, "inspect"),
            "Availability conditions differ and acceptance cannot be repeated");
        Check(!state.Trade.Present(inspectionQuest, "inspect", olderRadio),
            "Presenting cannot satisfy an inspection step");
        Check(state.Sell(state.Quote(wrong)) && state.Quests.IsAvailable(deliveryQuest) &&
            state.Quests.Accept(deliveryQuest) && AtStep(state, deliveryQuest, "sell"),
            "Ordinary sale opens the second definition");
    }

    // Seed the same run on a later expedition, with the radio held and both quests accepted.
    seed = Payload(File.ReadAllText(slot));
    seed["location"] = (int)RunLocation.Search;
    seed["world"] = Payload(legacy)["world"];
    seed["held"] = "radio-a";
    seed["heldRotated"] = true;
    WriteSeed(slot, seed);
    using (var state = Load(folder))
    {
        Check(AtStep(state, inspectionQuest, "inspect") && AtStep(state, deliveryQuest, "sell") &&
            state.HeldItemRotated && !Call(state.Quests, "RecordInspection", "strip-a"),
            "Continuing mid-step restores both quests and rejects a different inspected instance");
        FailWrite(() => Call(state.Quests, "RecordInspection", "radio-a"), state,
            () => AtStep(state, inspectionQuest, "inspect") && state.HeldItemId == "radio-a",
            "Failed inspection write preserves the step and hand");
        Check(Call(state.Quests, "RecordInspection", "radio-a") && AtStep(state, inspectionQuest, "present") &&
            AtStep(state, deliveryQuest, "sell") && state.HeldItemRotated &&
            !Call(state.Quests, "RecordInspection", "radio-a"), "Inspection advances once and preserves rotation");
        Check(Call(state, "Pack", new BagPlacement(0, 0, 2, 1, false)) &&
            Call(state, "ReturnHome", 2) && Call(state, "TravelBetweenSettlements", RunLocation.Trader) &&
            state.Home.Contains(radio), "Stock inherits the inspected find's exact expedition and key");
    }

    using (var state = Load(folder))
    {
        Check(AtStep(state, inspectionQuest, "present") && AtStep(state, deliveryQuest, "sell") &&
            !state.Quests.HasContact("test-technician"), "Reload keeps both current steps");
        var previous = File.ReadAllText(slot);
        Check(!state.Trade.Present(inspectionQuest, "present", sale) &&
            !state.Trade.Present(inspectionQuest, "present", new HomeItem { expedition = 3, key = radio.key }) &&
            !state.Trade.Deliver(inspectionQuest, "present", radio) && File.ReadAllText(slot) == previous,
            "Wrong stock, a missing instance and the wrong action leave bytes intact");
        var cash = state.Balance;
        FailWrite(() => state.Trade.Present(inspectionQuest, "present", radio), state,
            () => AtStep(state, inspectionQuest, "present") && !state.Quests.HasContact("test-technician") &&
                state.Home.Contains(radio), "Failed presentation write preserves stock, step and contact");
        Check(state.Trade.Present(inspectionQuest, "present", radio) &&
            state.Home.Contains(radio) && state.Home.Contains(olderRadio) && state.Balance == cash &&
            AtStep(state, inspectionQuest, "") && state.Quests.HasContact("test-technician") &&
            !state.Trade.Present(inspectionQuest, "present", radio), "Presentation keeps both radios and opens one contact");

        var offer = state.Quote(sale);
        FailWrite(() => state.Sell(offer), state,
            () => AtStep(state, deliveryQuest, "sell") && state.Home.Contains(sale) && state.Balance == cash,
            "Failed sale write preserves the sale step, stock and cash");
        Check(state.Sell(offer) && !state.Sell(offer) && state.Balance == cash + offer.Price &&
            !state.Home.Contains(sale) && AtStep(state, deliveryQuest, "deliver"),
            "Sale and quest step are published together exactly once");
        cash = state.Balance;
        FailWrite(() => state.Trade.Deliver(deliveryQuest, "deliver", radio), state,
            () => AtStep(state, deliveryQuest, "deliver") && state.Home.Contains(radio) && state.Balance == cash,
            "Failed delivery write preserves the item, step and reward");
        Check(!state.Trade.Present(deliveryQuest, "deliver", radio) &&
            state.Trade.Deliver(deliveryQuest, "deliver", radio) &&
            !state.Trade.Deliver(deliveryQuest, "deliver", olderRadio) &&
            !state.Home.Contains(radio) && state.Home.Contains(olderRadio) &&
            AtStep(state, deliveryQuest, "") && state.Balance == cash + 15,
            "Delivery consumes only the selected instance and pays the reward once");
    }
    using (var state = Load(folder))
    {
        var saved = Payload(File.ReadAllText(slot));
        Check((int)saved["format"] == 6 && (bool)saved["hasSold"] && saved["contacts"].Count() == 1 &&
            AtStep(state, inspectionQuest, "") && AtStep(state, deliveryQuest, "") &&
            state.Quests.HasContact("test-technician") && state.Home.SequenceEqual(new[] { olderRadio }) &&
            state.Balance == (long)saved["cash"], "The current format restores both completed quests and their results");
    }
    seed = Payload(File.ReadAllText(slot));
    seed["format"] = 5;
    seed["hasBackpack"] = true;
    seed.Remove("story");
    seed.Remove("secondYard");
    var format5Folder = Path.Combine(root, "format5");
    Directory.CreateDirectory(format5Folder);
    var format5Slot = Path.Combine(format5Folder, "slot.json");
    WriteSeed(format5Slot, seed);
    using (var state = Load(format5Folder))
    {
        Check(state.Location == RunLocation.Trader && state.HasBackpack && state.Bag.Height == 3 &&
            state.Quests.HasContact("test-technician") && state.Home.SequenceEqual(new[] { olderRadio }),
            "Format 5 preserves the backpack, location, stock and contacts");
        Check(Call(state, "Save"), "The migrated slot can be published");
        var saved = Payload(File.ReadAllText(format5Slot));
        Check((int)saved["format"] == 6 && (int)saved["story"] == 0 && !(bool)saved["secondYard"],
            "Format 5 migrates without inventing clues or moving the player");
    }
}
finally
{
    Environment.SetEnvironmentVariable(slotVariable, previousFolder);
}

return new { passed = checks.Count, checks, root };

GameState Load(string directory)
{
    Environment.SetEnvironmentVariable(slotVariable, directory);
    var state = (GameState)Activator.CreateInstance(typeof(GameState), members, null, new[] { loot, catalog }, null);
    if (Call(state, "LoadSlot"))
        return state;
    state.Dispose();
    throw new InvalidOperationException(state.Error);
}

bool Call(object target, string method, params object[] args) =>
    (bool)target.GetType().GetMethod(method, members).Invoke(target, args);

bool AtStep(GameState state, string id, string step) =>
    state.Quests.TryGetProgress(id, out var current) && (current ?? "") == step;

void FailWrite(Func<bool> command, GameState state, Func<bool> unchanged, string label)
{
    var before = File.ReadAllText(slot);
    using var blockade = new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Check(!command() && state.Error != null && unchanged() && File.ReadAllText(slot) == before, label);
}

void Check(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
    checks.Add(label);
}

JObject Payload(string envelope) => JObject.Parse((string)JObject.Parse(envelope)["payload"]);

void WriteSeed(string path, JObject document)
{
    var payload = document.ToString(Newtonsoft.Json.Formatting.None);
    using var hash = SHA256.Create();
    var checksum = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    File.WriteAllText(path, new JObject { ["payload"] = payload, ["checksum"] = checksum }.ToString());
}
