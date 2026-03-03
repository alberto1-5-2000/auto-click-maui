namespace AutoClickMaui.Services;

public class MonitorInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class PointDto
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class RectDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class StartDetectionRequest
{
    public string Type { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string MonitorId { get; set; } = "";
    public string ExecutionMode { get; set; } = "any";
    public List<ActionStepDto> Actions { get; set; } = new();
    public int IntervalMs { get; set; } = 250;
    public int CooldownMs { get; set; } = 800;
    public bool RequireScreenChangeAfterClick { get; set; } = false;
}

public class ActionStepDto
{
    public string Name { get; set; } = "";
    public string TemplateBase64 { get; set; } = "";
    public PointDto ClickPoint { get; set; } = new();
    public double Threshold { get; set; } = 0.88;
}

public class AutoClickProfile
{
    public string Name { get; set; } = "";
    public string MonitorId { get; set; } = "";
    public string ExecutionMode { get; set; } = "any";
    public List<ActionStepDto> Actions { get; set; } = new();
    public int IntervalMs { get; set; } = 250;
    public int CooldownMs { get; set; } = 800;
    public bool RequireScreenChangeAfterClick { get; set; } = false;
}

public class SaveProfileRequest
{
    public string Type { get; set; } = "";
    public string RequestId { get; set; } = "";
    public AutoClickProfile Profile { get; set; } = new();
}
