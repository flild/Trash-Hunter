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

        public SaleOffer Quote(HomeItem item) => new(item, catalog[item.key].Definition);

        public bool Present(string questId, string stepId, HomeItem item) =>
            state.Quests.Confirm(questId, stepId, QuestAction.Present, item);

        public bool Deliver(string questId, string stepId, HomeItem item) =>
            state.Quests.Confirm(questId, stepId, QuestAction.Deliver, item);

        public bool Sell(SaleOffer offer)
        {
            if (!catalog.TryGetDefinition(offer.Item.key ?? string.Empty, out var definition) ||
                definition.Price != offer.Price)
                return false;

            return state.CompleteSale(offer.Item, definition.Price);
        }
    }
}
