using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CirithSim
{
    // Campaign only: keeps at least one copy of every Cirith armor piece and her sword in the player's party inventory at all times.
    // Whenever the inventory could have changed (session start, game load, inventory screen closed, item sold, every game hour) any missing piece is added back.
    public class CirithInventoryBehavior : CampaignBehaviorBase
    {
        public static readonly string[] Pieces = { "cirith_head", "cirith_body", "cirith_legs", "cirith_hands", "cirith_sword" };      // the four armor pieces AND her blade

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => Ensure("session launched"));
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, _ => Ensure("game loaded"));
            CampaignEvents.PlayerInventoryExchangeEvent.AddNonSerializedListener(this, (a, b, c) => Ensure("inventory closed"));
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, () => Ensure("hourly"));
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => Ensure("daily"));
            // anything can remove an item between those events (selling, a lost battle, a stolen stash, an equip swap): a cheap check about once a second, but never while the inventory screen is open
            CampaignEvents.TickEvent.AddNonSerializedListener(this, dt => { acc += dt; if (acc < 1f) return; acc = 0; if (!InventoryScreenOpen()) Ensure("watch"); });
        }
        float acc;
        static bool InventoryScreenOpen() { try { var t = TaleWorlds.ScreenSystem.ScreenManager.TopScreen; return t != null && t.GetType().Name.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0; } catch { return false; } }
        public override void SyncData(IDataStore dataStore) { }

        public static void Ensure(string why) { try { var p = MobileParty.MainParty; if (p != null) EnsureRoster(p.ItemRoster, why); } catch (Exception e) { SimSubModule.L("inventory keeper error: " + e.Message); } }

        // returns how many pieces were added; roster is any ItemRoster (used directly by the headless test)
        public static int EnsureRoster(ItemRoster roster, string why)
        {
            int added = 0;
            foreach (var id in Pieces)
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(id); if (item == null) continue;
                if (roster.GetItemNumber(item) < 1) { roster.AddToCounts(item, 1); added++; SimSubModule.L($"inventory keeper ({why}): re-added {id}"); }
            }
            return added;
        }
    }
}
