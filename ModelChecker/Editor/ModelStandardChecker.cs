using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/*
 * ModelStandardChecker.cs
 *
 * 作者:阿灿
 * 创建日期: 2023-06-15
 *
 * 描述:
 * 主窗口类，负责UI展示和协调各组件工作
 */
public class ModelStandardChecker : EditorWindow
{
    public List<ModelIssue> issues = new List<ModelIssue>();
    private bool isChecking = false;
    private ModelCheckConfig config = new ModelCheckConfig();

    // 状态栏文本
    private string statusText = "就绪";

    // 检查结果统计
    private Dictionary<IssueType, int> issueStats = new Dictionary<IssueType, int>();

    // 列表视图状态（纯 UI 层，不参与检查逻辑）
    private int itemsPerPage = 10;           // 每页显示的问题数量
    private int currentPage = 0;             // 结果表格当前页
    private int severityFilter = -1;         // -1 = 全部；0 高 / 1 中 / 2 低 / 3 建议
    private IssueType? typeFilter = null;    // null = 全部类型
    private Vector2 tableScroll = Vector2.zero;
    private Vector2 sidebarScroll = Vector2.zero;

    // 轴心偏移检测
    private int totalTriangles = 0;
    private int totalObjects = 0;

    private HashSet<Texture2D> checkedTextures = new HashSet<Texture2D>();
    public HashSet<Material> materialSet = new HashSet<Material>();

    // 配置区默认折叠（低频操作，视觉权重低于结果区）
    [SerializeField] private bool configPanelFoldout = false;

    // 材质使用统计相关变量
    private Dictionary<Material, int> materialUsageStats = new Dictionary<Material, int>();
    private int totalMaterialAssignments = 0;
    private float averageTrianglesPerMaterial = 0f;

    [MenuItem("工具/模型规范检查器")]
    public static void ShowWindow()
    {
        var win = GetWindow<ModelStandardChecker>("模型规范检查器");
        win.minSize = new Vector2(860f, 560f);
    }

    #region 绘制窗体

    private void OnGUI()
    {
        // 布局：顶部命令栏 → 配置区 → (左)摘要栏 + (右)数据表格 → 状态栏
        UIDrawer.BeginApp();

        switch (UIDrawer.DrawTopBar(this, isChecking, configPanelFoldout))
        {
            case UIDrawer.UiAction.StartCheck:
                StartModelCheck();
                break;
            case UIDrawer.UiAction.ClearResults:
                ClearResultsWithConfirm();
                break;
            case UIDrawer.UiAction.ExportReport:
                ExportReport();
                break;
            case UIDrawer.UiAction.ToggleConfig:
                configPanelFoldout = !configPanelFoldout;
                break;
        }

        UIDrawer.DrawConfigPanel(ref config, ref configPanelFoldout, ref itemsPerPage);

        EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // 侧栏先画：用户点击筛选的瞬间就会改到 severityFilter / typeFilter，
        // 同一帧里紧接着再算 rows，避免"筛选变了、表格还显示上一帧结果"的闪烁。
        bool filterChanged = UIDrawer.DrawSidebar(issues, issueStats, totalTriangles, totalObjects, materialSet,
            averageTrianglesPerMaterial, isChecking,
            ref severityFilter, ref typeFilter, ref sidebarScroll);

        if (filterChanged)
        {
            // 筛选条件变了：回到第 1 页，并把表格滚回顶部
            currentPage = 0;
            tableScroll = Vector2.zero;
        }

        // 当前视图（筛选后的行）
        List<ModelIssue> rows = GetVisibleIssues();

        UIDrawer.DrawWorkspace(rows, issues.Count, isChecking,
            ref severityFilter, ref typeFilter, ref currentPage, ref itemsPerPage, ref tableScroll);

        EditorGUILayout.EndHorizontal();

        UIDrawer.DrawStatusBar(statusText, isChecking, issues.Count, rows.Count);

        UIDrawer.EndApp();
    }

    /// <summary>
    /// 视图层筛选 + 排序：严重度高的在前。
    /// 只影响列表显示，不改变任何检查结果。
    /// </summary>
    private List<ModelIssue> GetVisibleIssues()
    {
        IEnumerable<ModelIssue> q = issues;

        if (severityFilter >= 0)
            q = q.Where(i => UIDrawer.GetSeverityLevel(i.type) == severityFilter);

        if (typeFilter.HasValue)
            q = q.Where(i => i.type == typeFilter.Value);

        return q.OrderBy(i => UIDrawer.GetSeverityOrder(i.type)).ToList();
    }

    /// <summary>清空属于不可撤销操作，先二次确认</summary>
    private void ClearResultsWithConfirm()
    {
        if (issues.Count == 0) return;

        if (EditorUtility.DisplayDialog("清空检查结果",
            $"确定清空当前 {issues.Count} 条检查结果吗？此操作不可撤销。", "清空", "取消"))
        {
            ClearResults();
        }
    }

    #endregion

    #region 检查方法

    private void StartModelCheck()
    {
        if (isChecking) return;

        isChecking = true;
        statusText = "正在检查场景模型…";
        issues.Clear();
        issueStats.Clear();
        currentPage = 0;
        severityFilter = -1;
        typeFilter = null;
        tableScroll = Vector2.zero;
        sidebarScroll = Vector2.zero;
        checkedTextures.Clear();
        materialSet.Clear();
        materialUsageStats.Clear();
        totalMaterialAssignments = 0;
        averageTrianglesPerMaterial = 0f;

        try
        {
            // 获取场景中所有的MeshRenderer和SkinnedMeshRenderer
            var meshRenderers = FindObjectsOfType<MeshRenderer>();
            var skinnedMeshRenderers = FindObjectsOfType<SkinnedMeshRenderer>();
            var meshFilters = FindObjectsOfType<MeshFilter>();

            ModelChecker.CountTriangles(meshRenderers, skinnedMeshRenderers, ref totalTriangles, ref totalObjects);
            int totalObjectsCount = meshRenderers.Length + skinnedMeshRenderers.Length;
            int processedObjects = 0;

            // 检查MeshRenderer
            foreach (var renderer in meshRenderers)
            {
                statusText = $"正在检查: {renderer.name}";
                EditorUtility.DisplayProgressBar("检查模型", $"正在检查: {renderer.name}",
                    (float)processedObjects / totalObjectsCount);
                ModelChecker.CheckMeshRenderer(renderer, config, issues, checkedTextures, materialSet);
                processedObjects++;
            }

            // 检查SkinnedMeshRenderer
            foreach (var renderer in skinnedMeshRenderers)
            {
                statusText = $"正在检查: {renderer.name}";
                EditorUtility.DisplayProgressBar("检查模型", $"正在检查: {renderer.name}",
                    (float)processedObjects / totalObjectsCount);
                ModelChecker.CheckSkinnedMeshRenderer(renderer, config, issues, checkedTextures, materialSet);
                processedObjects++;
            }

            // 检查材质使用效率
            if (config.checkMaterialUsage)
            {
                statusText = "正在分析材质使用效率…";
                EditorUtility.DisplayProgressBar("检查模型", "正在分析材质使用效率...", 0.95f);
                ModelChecker.CheckMaterialUsage(
                    meshRenderers.Cast<Renderer>().Concat(skinnedMeshRenderers.Cast<Renderer>()).ToArray(),
                    meshFilters, config, issues, materialSet, ref materialUsageStats, 
                    ref totalMaterialAssignments, ref averageTrianglesPerMaterial);
            }

            // 统计问题类型
            foreach (var issue in issues)
            {
                if (issueStats.ContainsKey(issue.type))
                    issueStats[issue.type]++;
                else
                    issueStats[issue.type] = 1;
            }

            EditorUtility.ClearProgressBar();
            statusText = $"检查完成 · 共发现 {issues.Count} 个问题";
            Debug.Log($"模型检查完成！共发现 {issues.Count} 个问题");
        }
        finally
        {
            isChecking = false;
            EditorUtility.ClearProgressBar();
        }
    }

    #endregion

    public void ClearResults()
    {
        issues.Clear();
        issueStats.Clear();
        currentPage = 0;
        severityFilter = -1;
        typeFilter = null;
        tableScroll = Vector2.zero;
        sidebarScroll = Vector2.zero;
        totalTriangles = 0;
        totalObjects = 0;
        statusText = "已清空结果";
    }

  public void ExportReport()
{
    string path = EditorUtility.SaveFilePanel("保存检查报告", Application.dataPath, "模型检查报告", "xlsx");
    if (string.IsNullOrEmpty(path)) return;

    // 检查文件是否已存在且被占用
    if (File.Exists(path))
    {
        if (IsFileInUse(path))
        {
            // 如果文件被占用，尝试关闭相关进程
            if (!CloseExcelProcesses())
            {
                // 如果无法关闭进程，提示用户手动处理
                if (!EditorUtility.DisplayDialog("文件已打开", 
                    "Excel文件正在被使用，请先关闭它，然后点击确定重试。", 
                    "确定", "取消"))
                {
                    return; // 用户取消操作
                }
                
                // 再次检查文件是否仍被占用
                if (IsFileInUse(path))
                {
                    EditorUtility.DisplayDialog("无法保存", "文件仍然被占用，请手动关闭后重试。", "确定");
                    return;
                }
            }
        }
        
        // 确保文件被删除后再创建新文件
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("删除文件失败", $"无法删除现有文件: {ex.Message}", "确定");
            return;
        }
    }

    try
    {
        // 生成新的Excel报告
        ReportExporter.GenerateExcelReport(path, issues, issueStats, totalTriangles, totalObjects);
        Debug.Log($"报告已保存到: {path}");
        
        // 打开生成的报告
        System.Diagnostics.Process.Start(path);
    }
    catch (Exception ex)
    {
        Debug.LogError($"导出报告失败: {ex.Message}");
        EditorUtility.DisplayDialog("导出失败", $"生成报告时出错: {ex.Message}", "确定");
    }
}

// 检查文件是否被占用
private bool IsFileInUse(string path)
{
    if (!File.Exists(path)) return false;
    
    try
    {
        using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Close();
        }
    }
    catch (IOException)
    {
        // 文件被占用时会抛出IOException
        return true;
    }
    catch
    {
        return false;
    }
    
    return false;
}

// 尝试关闭所有Excel进程
private bool CloseExcelProcesses()
{
    try
    {
        // 获取所有Excel进程
        var processes = System.Diagnostics.Process.GetProcessesByName("EXCEL");
        if (processes.Length == 0) return false;
        
        // 询问用户是否关闭Excel进程
        if (EditorUtility.DisplayDialog("关闭Excel", 
            "检测到Excel正在运行，需要关闭它才能替换文件。是否关闭所有Excel窗口？", 
            "是", "否"))
        {
            foreach (var process in processes)
            {
                process.CloseMainWindow();
                // 等待进程关闭
                process.WaitForExit(5000);
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            return true;
        }
    }
    catch (Exception ex)
    {
        Debug.LogError($"关闭Excel进程失败: {ex.Message}");
    }
    return false;
}
}
