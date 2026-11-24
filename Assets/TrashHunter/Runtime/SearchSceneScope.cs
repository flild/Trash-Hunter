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

        private static LifetimeScope runScope;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetScope() => runScope = null;

        protected override LifetimeScope FindParent()
        {
            if (runScope != null)
                return runScope;

            var appScope = Create(name: "Application Scope");
            DontDestroyOnLoad(appScope.gameObject);
            runScope = appScope.CreateChild(
                builder => builder.Register<GameState>(Lifetime.Singleton), "Run Scope");
            return runScope;
        }

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterBuildCallback(resolver =>
            {
                var player = resolver.Instantiate(playerPrefab, transform);
                var hud = resolver.Instantiate(hudPrefab, transform);
                session.Initialize(resolver.Resolve<GameState>(), player, hud);
            });
        }
    }
}
