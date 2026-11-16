using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace AbominableSnowman
{
    /// <summary>Finds the player cars (each has a networked VehicleInput) and throws them.</summary>
    internal static class Cars
    {
        internal sealed class Car
        {
            public VehicleInput Input;
            public Rigidbody Body;
            public int Player;           // PlayerRef.Raw of the driver
            public bool IsLocal;         // we drive it
            public bool HasStateAuthority;
        }

        private static readonly List<Car> _cars = new();
        private static float _nextScan;

        public static Car Local { get; private set; }
        public static IReadOnlyList<Car> All => _cars;

        public static void Update()
        {
            if (Time.unscaledTime < _nextScan && !AnyDead()) { RefreshAuthority(); return; }
            _nextScan = Time.unscaledTime + 1f;

            _cars.Clear();
            Local = null;
            foreach (var vi in Object.FindObjectsOfType<VehicleInput>())
            {
                if (vi == null) continue;
                var car = new Car { Input = vi };
                car.Body = vi.GetComponentInParent<Rigidbody>();
                if (car.Body == null) car.Body = vi.transform.root.GetComponentInChildren<Rigidbody>();
                if (car.Body == null) continue;
                if (!ReadAuthority(car)) continue;
                _cars.Add(car);
                if (car.IsLocal) Local = car;
            }
        }

        private static bool AnyDead()
        {
            foreach (var c in _cars)
                if (c.Input == null || c.Input.WasCollected || c.Body == null || c.Body.WasCollected) return true;
            return false;
        }

        private static void RefreshAuthority()
        {
            foreach (var c in _cars) ReadAuthority(c);
        }

        private static bool ReadAuthority(Car car)
        {
            try
            {
                var obj = car.Input.Object;
                if (obj == null) return false;
                car.IsLocal = obj.HasInputAuthority;
                car.HasStateAuthority = obj.HasStateAuthority;
                car.Player = obj.InputAuthority.Raw;
                return true;
            }
            catch
            {
                return false; // not spawned on the network yet
            }
        }

        public static Car ByPlayer(int player)
        {
            foreach (var c in _cars)
                if (c.Player == player) return c;
            return null;
        }

        /// <summary>Launch velocity that carries a body <paramref name="distance"/> m on flat ground.</summary>
        public static Vector3 ThrowVelocity(float headingDeg, float distance, float angleDeg)
        {
            float g = Mathf.Max(1f, Physics.gravity.magnitude);
            float a = Mathf.Clamp(angleDeg, 10f, 80f) * Mathf.Deg2Rad;
            float speed = Mathf.Sqrt(distance * g / Mathf.Sin(2f * a));
            var flat = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward;
            return flat * (speed * Mathf.Cos(a)) + Vector3.up * (speed * Mathf.Sin(a));
        }

        public static void Apply(Car car, Vector3 velocity, Vector3 spin)
        {
            var rb = car?.Body;
            if (rb == null || rb.isKinematic) return;
            rb.WakeUp();
            // Lift slightly so the wheels don't drag on the launch frame.
            rb.position += Vector3.up * 0.4f;
            rb.linearVelocity = velocity;
            rb.angularVelocity = spin;
        }
    }
}
