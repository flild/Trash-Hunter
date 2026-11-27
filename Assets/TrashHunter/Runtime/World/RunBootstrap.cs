using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TrashHunter
{
    internal static class RunBootstrap
    {
        public static LifetimeScope RunScope { get; private set; }
        internal static SceneCoordinator Scenes { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            RunScope = null;
            Scenes = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Start()
        {
            var catalog = LootCatalog.Load();
            var quests = QuestCatalog.Load(catalog);
            var application = LifetimeScope.Create(builder =>
            {
                builder.RegisterInstance(catalog);
                builder.RegisterInstance(quests);
            }, "Application Scope");
            Object.DontDestroyOnLoad(application.gameObject);
            RunScope = application.CreateChild(builder =>
                builder.Register(resolver => new GameState(resolver.Resolve<LootCatalog>(),
                    resolver.Resolve<QuestCatalog>()), Lifetime.Singleton), "Run Scope");
            Scenes = application.gameObject.AddComponent<SceneCoordinator>();
            Scenes.Initialize(RunScope.Container.Resolve<GameState>(), catalog);
        }
    }
}
