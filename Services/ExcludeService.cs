public class ExcludeService
{
    public HashSet<string> ExcludedItems { get; private set; } = new();
    private readonly string _path;

    public ExcludeService(string path)
    {
        _path = path;
        Load();
    }

    public void Load()
    {
        if (File.Exists(_path))
        {
            ExcludedItems = new HashSet<string>(File.ReadAllLines(_path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim()));
        }
    }

    public void Save()
    {
        // Создаем директорию если не существует
        var directory = Path.GetDirectoryName(_path);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(_path, ExcludedItems);
    }

    public void Add(string item)
    {
        if (ExcludedItems.Add(item))
            Save();
    }

    public bool Remove(string item)
    {
        if (ExcludedItems.Remove(item))
        {
            Save();
            return true;
        }
        return false;
    }

    public bool Contains(string item)
    {
        return ExcludedItems.Contains(item);
    }

    public void Clear()
    {
        ExcludedItems.Clear();
        Save();
    }
}