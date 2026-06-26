using Godot;
using System;
using System.Collections.Generic;

namespace OpenFo3.World
{
    public partial class PickHandler : Node
    {
        [Signal]
        public delegate void ObjectPickedEventHandler(Node3D pickedNode, Vector3 position, uint formId);

        private Camera3D _camera;
        private float _pickRange = 200f;

        public override void _Ready()
        {
            SetProcess(false);
        }

        public Godot.Collections.Dictionary RaycastFromCenter()
        {
            _camera = GetViewport()?.GetCamera3D();
            if (_camera == null) return null;

            Vector2 screenCenter = GetViewport().GetVisibleRect().Size / 2f;
            return RaycastFromScreen(screenCenter);
        }

        public Godot.Collections.Dictionary RaycastFromScreen(Vector2 screenPos)
        {
            _camera = GetViewport()?.GetCamera3D();
            if (_camera == null) return null;

            Vector3 from = _camera.ProjectRayOrigin(screenPos);
            Vector3 dir = _camera.ProjectRayNormal(screenPos);

            var space = GetViewport().GetWorld3D().DirectSpaceState;
            var query = new PhysicsRayQueryParameters3D();
            query.From = from;
            query.To = from + dir * _pickRange;
            query.CollisionMask = 0xFFFFFFFF;
            query.HitFromInside = true;

            return space.IntersectRay(query);
        }

        public void PickObjectAtScreen(Vector2 screenPos)
        {
            var result = RaycastFromScreen(screenPos);
            if (result == null || result.Count == 0) return;

            Variant colliderVariant = result["collider"];
            if (colliderVariant.VariantType != Variant.Type.Object) return;
            var collider = colliderVariant.AsGodotObject() as Node3D;
            if (collider == null) return;

            Variant posVariant = result["position"];
            Vector3 pos = posVariant.AsVector3();

            uint formId = 0;
            Node3D current = collider;
            while (current != null)
            {
                string name = current.Name;
                if (name.StartsWith("Body_"))
                {
                    string hex = name.Substring(5);
                    if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out formId))
                        break;
                }
                current = current.GetParent() as Node3D;
            }

            EmitSignal(SignalName.ObjectPicked, collider, pos, formId);
        }
    }
}
