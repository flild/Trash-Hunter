namespace TrashHunter
{
    public sealed class TradeCounter
    {
        internal const int BackpackPrice = 45;
        private const string RepairerContact = "repairer";
        private readonly GameState state;
        private readonly LootCatalog catalog;

        internal TradeCounter(GameState state, LootCatalog catalog)
        {
            this.state = state;
            this.catalog = catalog;
        }

        public SaleOffer Quote(HomeItem item) => state.TryGetHomeItem(item, out var stored)
            ? new SaleOffer(stored, catalog[stored.key])
            : default;

        public bool Present(string questId, string stepId, HomeItem item) =>
            state.Quests.Confirm(questId, stepId, QuestAction.Present, item);

        public bool Deliver(string questId, string stepId, HomeItem item) =>
            state.Quests.Confirm(questId, stepId, QuestAction.Deliver, item);

        public int RepairPrice(HomeItem item)
        {
            if (state.Location != RunLocation.Trader || !state.Quests.HasContact(RepairerContact) ||
                !state.TryGetHomeItem(item, out var stored) || stored.IsRepaired)
                return 0;

            return catalog[stored.key].specialistFee;
        }

        public bool Repair(HomeItem item) => state.CompleteRepair(item, RepairPrice(item));

        public bool BuyBackpack() => state.CompleteBackpackPurchase(BackpackPrice);

        public bool Sell(SaleOffer offer)
        {
            var current = Quote(offer.Item);
            if (current.Price != offer.Price || current.Item.work != offer.Item.work)
                return false;

            return state.CompleteSale(current.Item, current.Price);
        }
    }
}
