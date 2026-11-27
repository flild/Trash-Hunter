using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TrashHunter
{
    public sealed class SearchSceneScope : LifetimeScope
    {
        [SerializeField] private SearchSession session;
        [SerializeField] private SearchPlayer playerPrefab;
        [SerializeField] private SearchHud hudPrefab;

        protected override LifetimeScope FindParent() => RunBootstrap.RunScope;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterBuildCallback(resolver =>
            {
                var player = resolver.Instantiate(playerPrefab, transform);
                var hud = resolver.Instantiate(hudPrefab, transform);
                session.Initialize(resolver.Resolve<GameState>(), resolver.Resolve<LootCatalog>(), player, hud);
            });
        }
    }
}
