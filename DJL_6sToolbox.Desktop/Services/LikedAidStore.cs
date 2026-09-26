using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;

namespace DJL_6sToolbox.Desktop.Services;

/// <summary>持久化已点赞视频 ID，避免重复点赞。</summary>
public sealed class LikedAidStore
{
    private readonly string _file = AppPaths.LikedAidsFile;
    private readonly object _lock = new();
    private readonly HashSet<long> _ids;

    public LikedAidStore()
    {
        AppPaths.EnsureCreated();
        _ids = Load();
    }

    private HashSet<long> Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var json = File.ReadAllText(_file);
                var list = JsonSerializer.Deserialize<List<long>>(json);
                return list is null ? new HashSet<long>() : new HashSet<long>(list);
            }
        }
        catch
        {
            // 忽略损坏文件。
        }

        return new HashSet<long>();
    }

    public bool Contains(long aid) => _ids.Contains(aid);

    public void Add(long aid)
    {
        lock (_lock)
        {
            if (_ids.Add(aid))
            {
                SaveLocked();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _ids.Count;
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            File.WriteAllText(_file, JsonSerializer.Serialize(_ids.OrderBy(x => x).ToList()));
        }
        catch
        {
            // 保存失败不应中断点赞流程。
        }
    }
}
