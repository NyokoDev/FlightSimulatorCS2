using Game;
using UnityEngine;


namespace CS2FlightTakeover.UI
{
    // Creates the in-game MonoBehaviour panel once the ECS world is active.
    public sealed partial class PanelBootstrapSystem : GameSystemBase
    {
        private GameObject? _panelObject;

        protected override void OnUpdate()
        {
            if (_panelObject != null) return;
            _panelObject = new GameObject("CS2FlightTakeoverPanel");
            Object.DontDestroyOnLoad(_panelObject);
        }

        protected override void OnDestroy()
        {
            if (_panelObject != null) Object.Destroy(_panelObject);
            base.OnDestroy();
        }
    }

}
