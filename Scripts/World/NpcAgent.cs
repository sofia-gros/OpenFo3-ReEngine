using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenFo3.World
{
    public partial class NpcAgent : CharacterBody3D
    {
        private NavigationAgent3D _navAgent;
        private AnimationPlayer _animPlayer;
        private Skeleton3D _skeleton;
        private MeshInstance3D _meshInstance;

        private Vector3 _targetPosition;
        private bool _hasTarget;
        private float _idleTimer;
        private Random _rng = new();

        [Export]
        public float MovementSpeed { get; set; } = 2.0f;

        [Export]
        public float IdleMinTime { get; set; } = 3.0f;

        [Export]
        public float IdleMaxTime { get; set; } = 10.0f;

        [Export]
        public float WanderRadius { get; set; } = 15.0f;

        public override void _Ready()
        {
            _navAgent = GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D");
            if (_navAgent == null)
            {
                _navAgent = new NavigationAgent3D();
                _navAgent.Name = "NavigationAgent3D";
                AddChild(_navAgent);
            }

            _skeleton = GetNodeOrNull<Skeleton3D>("Skeleton3D");
            _animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            _meshInstance = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");

            _idleTimer = GetRandomIdleTime();

            _navAgent.TargetReached += OnTargetReached;
            _navAgent.NavigationFinished += OnNavigationFinished;
            _navAgent.VelocityComputed += OnVelocityComputed;

            if (_skeleton != null)
            {
                _skeleton.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_navAgent == null) return;

            if (_navAgent.IsNavigationFinished())
            {
                _idleTimer -= (float)delta;
                if (_idleTimer <= 0)
                {
                    PickRandomDestination();
                    _idleTimer = GetRandomIdleTime();
                }
                return;
            }

            Vector3 nextPos = _navAgent.GetNextPathPosition();
            Vector3 currentPos = GlobalPosition;
            Vector3 direction = (nextPos - currentPos).Normalized();
            direction.Y = 0;

            if (direction.LengthSquared() > 0.001f)
            {
                Vector3 velocity = direction * MovementSpeed;

                if (_skeleton != null)
                {
                    var lookTarget = currentPos + direction;
                    _skeleton.LookAt(lookTarget, Vector3.Up);
                }

                _navAgent.Velocity = velocity;
            }
            else
            {
                _navAgent.Velocity = Vector3.Zero;
            }
        }

        private void OnTargetReached()
        {
            _navAgent.Velocity = Vector3.Zero;
        }

        private void OnNavigationFinished()
        {
            _navAgent.Velocity = Vector3.Zero;
        }

        private void OnVelocityComputed(Vector3 safeVelocity)
        {
            Velocity = safeVelocity;
            MoveAndSlide();
        }

        private void PickRandomDestination()
        {
            if (_navAgent == null) return;

            Vector3 origin = GlobalPosition;
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            float dist = (float)(_rng.NextDouble() * WanderRadius);

            Vector3 randomTarget = origin + new Vector3(
                Mathf.Cos(angle) * dist,
                0,
                Mathf.Sin(angle) * dist
            );

            _navAgent.TargetPosition = randomTarget;
        }

        private float GetRandomIdleTime()
        {
            return IdleMinTime + (float)_rng.NextDouble() * (IdleMaxTime - IdleMinTime);
        }

        public void SetTargetPosition(Vector3 target)
        {
            if (_navAgent != null)
            {
                _navAgent.TargetPosition = target;
                _idleTimer = float.MaxValue;
            }
        }

        public void AttachSkeleton(Node3D skeletonRoot)
        {
            if (skeletonRoot is Skeleton3D skel)
            {
                _skeleton = skel;
                return;
            }

            _skeleton = skeletonRoot.GetNodeOrNull<Skeleton3D>("Skeleton3D");
            if (_skeleton == null)
                _skeleton = skeletonRoot.GetChildren().OfType<Skeleton3D>().FirstOrDefault();
        }

        public void AttachAnimationPlayer(AnimationPlayer player)
        {
            _animPlayer = player;
        }
    }
}
