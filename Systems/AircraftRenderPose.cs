using Unity.Entities;
using Unity.Mathematics;

namespace CS2FlightTakeover.Systems
{
    // Render-only interpolation. The flight controller remains the authoritative
    // source for flight dynamics, instruments, terrain and ground operations.
    internal sealed class AircraftRenderPose
    {
        internal Entity Entity { get; private set; } = Unity.Entities.Entity.Null;
        internal float3 Position { get; private set; }
        internal quaternion Rotation { get; private set; } = quaternion.identity;
        private float3 fromPosition, toPosition;
        private quaternion fromRotation, toRotation;
        private float sampleTime, segmentStart, duration;
        private bool direct;

        internal void Reset() { Entity = Unity.Entities.Entity.Null; }

        private void Snap(Entity entity, float3 position, quaternion rotation, float now, bool exact)
        {
            Entity = entity;
            Position = fromPosition = toPosition = position;
            Rotation = fromRotation = toRotation = rotation;
            sampleTime = segmentStart = now;
            duration = 0f;
            direct = exact;
        }

        internal void Advance(Entity entity, float3 position, quaternion rotation, float now, float frameDelta, bool exact)
        {
            if (Entity != entity || exact || direct || now < sampleTime ||
                math.distancesq(position, toPosition) > 100f * 100f)
            {
                // No interpolation across session changes, wrap/return-to-map, or pushback/brake.
                Snap(entity, position, rotation, now, exact);
                return;
            }

            // Complete the previous segment before accepting the next sample. Its starting
            // point is the currently displayed pose, never a separately predicted position.
            float alpha = duration > 0f ? math.saturate((now - segmentStart) / duration) : 1f;
            Position = math.lerp(fromPosition, toPosition, alpha);
            Rotation = math.slerp(fromRotation, toRotation, alpha);

            bool changed = math.any(position != toPosition) ||
                math.any(rotation.value != toRotation.value);
            if (!changed) return;

            float interval = now - sampleTime;
            if (interval <= .0001f) interval = frameDelta;
            // Usually one rendered frame; cap extra presentation delay at 50 ms.
            duration = math.clamp(interval, 1f / 240f, .05f);
            fromPosition = Position;
            fromRotation = Rotation;
            toPosition = position;
            toRotation = rotation;
            sampleTime = segmentStart = now;
        }
    }
}
