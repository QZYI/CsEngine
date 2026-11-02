using Tiny3D.Graphics;
using Tiny3D.Math;
using Tiny3D.Tools;

namespace Tiny3D.Scene;

/// <summary>Scene-graph camera controller that synchronizes a construct pivot with a camera.</summary>
public sealed class CameraConstruct : TransformableConstruct
{
    internal override bool IsNameReadOnly => true;

    private readonly Camera _camera;
    private Vector3 _syncedCameraPosition;
    private Vector3 _syncedCameraTarget;
    private Vector3 _syncedPivotPosition;
    private Vector3 _syncedPivotRotation;

    public CameraConstruct(Camera camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        Pivot.Position = camera.Position;
        var direction = (camera.Target - camera.Position).Normalized();
        Pivot.Rotation = new Vector3(
            MathF.Asin(-direction.Y),
            MathF.Atan2(direction.X, direction.Z),
            0);
        SyncCamera();
        Name = "Camera";
    }

    [Inspect("Camera")]
    [Range(1, 179)]
    public int FieldOfView
    {
        get => (int)MathF.Round(_camera.FieldOfView);
        set
        {
            if (value is < 1 or > 179)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Field of view must be between 1 and 179 degrees.");
            }

            _camera.FieldOfView = value;
        }
    }

    [Inspect("Camera")]
    public bool LockedMovement { get; set; }

    [Inspect("Camera")]
    public Vector3 Position
    {
        get => Pivot.Position;
        set => Pivot.Position = value;
    }

    [Inspect("Camera")]
    public Vector3 Rotation
    {
        get => Pivot.Rotation;
        set => Pivot.Rotation = value;
    }

    /// <summary>Moves the camera by an offset in its local horizontal frame and world vertical axis.</summary>
    public void Move(Vector3 localOffset)
    {
        if (LockedMovement)
        {
            return;
        }

        var yaw = Pivot.Rotation.Y;
        var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        var forward = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        Pivot.Position += right * localOffset.X + Vector3.Up * localOffset.Y + forward * localOffset.Z;
    }

    /// <summary>Applies an Euler rotation offset in radians.</summary>
    public void Rotate(Vector3 rotationOffset)
    {
        if (!LockedMovement)
        {
            Pivot.Rotation += rotationOffset;
        }
    }

    /// <summary>Moves the camera along its screen-space right and up directions.</summary>
    public void Pan(Vector3 screenOffset)
    {
        if (LockedMovement)
        {
            return;
        }

        var rotation = new Pivot { Rotation = Pivot.Rotation };
        var right = rotation.Apply(new Vector3(1, 0, 0));
        var up = rotation.Apply(new Vector3(0, 1, 0));
        Pivot.Position += right * screenOffset.X + up * screenOffset.Y;
    }

    /// <summary>Synchronizes camera pose and field of view from the construct pivot.</summary>
    public void SyncCamera()
    {
        var cameraChanged = !_camera.Position.Equals(_syncedCameraPosition) ||
            !_camera.Target.Equals(_syncedCameraTarget);
        var pivotChanged = !Pivot.Position.Equals(_syncedPivotPosition) ||
            !Pivot.Rotation.Equals(_syncedPivotRotation);
        if (cameraChanged && !pivotChanged)
        {
            Pivot.Position = _camera.Position;
            var direction = (_camera.Target - _camera.Position).Normalized();
            Pivot.Rotation = new Vector3(
                MathF.Asin(-direction.Y),
                MathF.Atan2(direction.X, direction.Z),
                Pivot.Rotation.Z);
        }

        var rotation = new Pivot { Rotation = Pivot.Rotation };
        var forward = rotation.Apply(new Vector3(0, 0, 1)).Normalized();
        _camera.Position = Pivot.Position;
        _camera.Target = Pivot.Position + forward * 5;
        _camera.FieldOfView = FieldOfView;
        _syncedCameraPosition = _camera.Position;
        _syncedCameraTarget = _camera.Target;
        _syncedPivotPosition = Pivot.Position;
        _syncedPivotRotation = Pivot.Rotation;
    }

    protected override Construct CreateDuplicate() =>
        throw new NotSupportedException("The camera construct cannot be duplicated.");
}
