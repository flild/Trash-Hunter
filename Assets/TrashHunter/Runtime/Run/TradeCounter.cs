namespace TrashHunter
{
    public sealed class TradeCounter
    {
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

        public bool Sell(SaleOffer offer)
        {
            var current = Quote(offer.Item);
            if (current.Price != offer.Price || current.Item.work != offer.Item.work)
                return false;

            return state.CompleteSale(current.Item, current.Price);
        }
    }
}
