using System.Runtime.CompilerServices;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina;
using Lumina.Data;
using Lumina.Excel;

// Empty typed sheets exercise startup without reading game files or invoking native functions.
internal sealed class EmptyDataManager(Action onRead) : IDataManager
{
    public ClientLanguage Language => ClientLanguage.ChineseSimplified;
    public GameData GameData => throw new NotSupportedException();
    public ExcelModule Excel => throw new NotSupportedException();
    public bool HasModifiedGameDataFiles => false;
    public ExcelSheet<T> GetExcelSheet<T>(ClientLanguage? language = null, string? name = null) where T : struct, IExcelRow<T>
    {
        onRead();
        return new ExcelSheet<T>((RawExcelSheet)RuntimeHelpers.GetUninitializedObject(typeof(RawExcelSheet)));
    }
    public SubrowExcelSheet<T> GetSubrowExcelSheet<T>(ClientLanguage? language = null, string? name = null)
        where T : struct, IExcelSubrow<T> => throw new NotSupportedException();
    public FileResource? GetFile(string path) => throw new NotSupportedException();
    public T? GetFile<T>(string path) where T : FileResource => throw new NotSupportedException();
    public Task<T> GetFileAsync<T>(string path, CancellationToken cancellationToken) where T : FileResource => throw new NotSupportedException();
    public bool FileExists(string path) => false;
}
