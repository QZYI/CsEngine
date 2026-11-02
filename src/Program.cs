using System.Text.Json;
using Tiny3D;
using Tiny3D.Graphics;
using Tiny3D.Input;
using Tiny3D.Math;
using Tiny3D.Scene;

new PrototypeGame().Run();

internal sealed class PrototypeGame : Game
{
    private readonly Entity[] _movingCubes;
    private readonly Vector3[] _movingCubePositions;
    private float _elapsedSeconds;

    public PrototypeGame() : base(title: "Tiny3D - Scene Prototype")
    {
        CameraNode.Position = new Vector3(0, 3, -11);
        CameraNode.Rotation = new Vector3(0.23f, 0, 0);

        Scene.Add(LoadBaseplate());

        _movingCubes =
        [
            AddCube("Moving Cube A", new Vector3(-2, 0, 0), new Color32(65, 165, 245)),
            AddCube("Moving Cube B", new Vector3(2, 0, 1), new Color32(242, 151, 92)),
            AddCube("Moving Cube C", new Vector3(0, 0, 3), new Color32(132, 206, 184))
        ];
        _movingCubePositions = _movingCubes.Select(cube => cube.Pivot.Position).ToArray();

        AddCube("Static Cube A", new Vector3(-4, 0, 3), new Color32(188, 117, 207));
        AddCube("Static Cube B", new Vector3(4, 0, -2), new Color32(232, 199, 101));
        AddCube("Static Cube C", new Vector3(0, 1, -3), new Color32(207, 100, 111), 2);
    }

    protected override void Update(float deltaSeconds)
    {
        _elapsedSeconds += deltaSeconds;
        for (var index = 0; index < _movingCubes.Length; index++)
        {
            var cube = _movingCubes[index];
            cube.Pivot.Rotation += new Vector3(0.35f, 0.7f, 0.2f) * deltaSeconds;
            var bob = MathF.Sin(_elapsedSeconds * 1.5f + index) * 0.3f;
            cube.Pivot.Position = _movingCubePositions[index] + new Vector3(0, bob, 0);
        }

        var movement = Vector3.Zero;
        if (Input.IsKeyDown(Key.W))
        {
            movement += new Vector3(0, 0, 1);
        }
        if (Input.IsKeyDown(Key.S))
        {
            movement += new Vector3(0, 0, -1);
        }
        if (Input.IsKeyDown(Key.A))
        {
            movement += new Vector3(-1, 0, 0);
        }
        if (Input.IsKeyDown(Key.D))
        {
            movement += new Vector3(1, 0, 0);
        }
        if (Input.IsKeyDown(Key.E))
        {
            movement += new Vector3(0, 1, 0);
        }
        if (Input.IsKeyDown(Key.Q))
        {
            movement += new Vector3(0, -1, 0);
        }

        CameraNode.Move(movement * (5 * deltaSeconds));

        var rotation = Vector3.Zero;
        if (Input.IsKeyDown(Key.Up))
        {
            rotation += new Vector3(-1, 0, 0);
        }
        if (Input.IsKeyDown(Key.Down))
        {
            rotation += new Vector3(1, 0, 0);
        }
        if (Input.IsKeyDown(Key.Left))
        {
            rotation += new Vector3(0, -1, 0);
        }
        if (Input.IsKeyDown(Key.Right))
        {
            rotation += new Vector3(0, 1, 0);
        }
        CameraNode.Rotate(rotation * (1.5f * deltaSeconds));
        if (Input.IsRightMouseButtonDown)
        {
            CameraNode.Rotate(new Vector3(
                -Input.MouseDeltaY * 0.005f,
                Input.MouseDeltaX * 0.005f,
                0));
        }
        if (Input.IsMiddleMouseButtonDown)
        {
            CameraNode.Pan(new Vector3(
                -Input.MouseDeltaX * 0.02f,
                -Input.MouseDeltaY * 0.02f,
                0));
        }
        if (Input.MouseWheelDelta != 0)
        {
            CameraNode.Move(new Vector3(0, 0, Input.MouseWheelDelta / 120f * 0.8f));
        }
        if (Input.WasKeyPressed(Key.Escape))
        {
            Exit();
        }
    }

    private Entity AddCube(string name, Vector3 position, Color32 color, float size = 1)
    {
        var cube = new Entity(Mesh.CreateCube(size))
        {
            Name = name,
            Color = color
        };
        cube.Pivot.Position = position;
        Scene.Add(cube);
        return cube;
    }

    private static Entity LoadBaseplate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "default-template-settings.json");
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<DefaultTemplateSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException($"Template settings file '{path}' is empty.");
        var baseplate = settings.Baseplate
            ?? throw new InvalidDataException($"Template settings file '{path}' is missing baseplate settings.");
        if (string.IsNullOrWhiteSpace(baseplate.Name) ||
            !float.IsFinite(baseplate.Position.X) || !float.IsFinite(baseplate.Position.Y) ||
            !float.IsFinite(baseplate.Position.Z) ||
            !float.IsFinite(baseplate.Scale.X) || !float.IsFinite(baseplate.Scale.Y) ||
            !float.IsFinite(baseplate.Scale.Z) || baseplate.Scale.X <= 0 ||
            baseplate.Scale.Y <= 0 || baseplate.Scale.Z <= 0)
        {
            throw new InvalidDataException($"Template settings file '{path}' has invalid baseplate values.");
        }

        var entity = new Entity(Mesh.CreateCube())
        {
            Name = baseplate.Name,
            Color = new Color32(
                baseplate.Color.Red,
                baseplate.Color.Green,
                baseplate.Color.Blue,
                baseplate.Color.Alpha)
        };
        entity.Pivot.Position = new Vector3(
            baseplate.Position.X, baseplate.Position.Y, baseplate.Position.Z);
        entity.Pivot.Scale = new Vector3(
            baseplate.Scale.X, baseplate.Scale.Y, baseplate.Scale.Z);
        return entity;
    }
}

internal sealed class DefaultTemplateSettings
{
    public BaseplateSettings Baseplate { get; init; } = new();
}

internal sealed class BaseplateSettings
{
    public string Name { get; init; } = "Baseplate";
    public BaseplateColor Color { get; init; } = new();
    public BaseplateVector Position { get; init; } = new() { Y = -0.6f };
    public BaseplateVector Scale { get; init; } = new() { X = 12, Y = 0.2f, Z = 12 };
}

internal sealed class BaseplateColor
{
    public byte Red { get; init; } = 82;
    public byte Green { get; init; } = 91;
    public byte Blue { get; init; } = 100;
    public byte Alpha { get; init; } = 255;
}

internal sealed class BaseplateVector
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
}
