using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Style;

/*
 * ReportExporter.cs
 *
 * 作者: 阿灿
 * 创建日期: 2023-06-15
 *
 * 描述:
 * 负责生成和导出检查报告，优化版本
 * 支持灵活的统计信息扩展和美观的表格排版
 */
public static class ReportExporter
{
    private static readonly string TECHNICAL_SUPPORT = "技术支持：阿灿";
    
    public static void GenerateExcelReport(string path, List<ModelIssue> issues, 
                                          Dictionary<IssueType, int> issueStats, 
                                          int totalTriangles, int totalObjects)
    {
        using (var package = new ExcelPackage())
        {
            CreateOverviewSheet(package, issues, issueStats, totalTriangles, totalObjects);
            CreateDetailSheets(package, issues);
            
            package.SaveAs(new FileInfo(path));
        }
    }
    
    #region 总览表创建
    
    private static void CreateOverviewSheet(ExcelPackage package, List<ModelIssue> issues, 
                                          Dictionary<IssueType, int> issueStats, 
                                          int totalTriangles, int totalObjects)
    {
        var sheet = package.Workbook.Worksheets.Add("总览统计");
        
        // 报告标题区域
        CreateReportHeader(sheet);
        
        // 基础统计信息
        CreateBasicStatistics(sheet, issues, issueStats, totalTriangles, totalObjects);
        
        // 问题分类统计
        CreateCategoryStatistics(sheet, issues, issueStats);
        
        sheet.Cells.AutoFitColumns();
    }
    
    private static void CreateReportHeader(ExcelWorksheet sheet)
    {
        var currentRow = 1;
        
        // 主标题
        sheet.Cells[currentRow, 1].Value = $"Unity 模型规范检查报告    {TECHNICAL_SUPPORT}";
        ApplyTitleStyle(sheet.Cells[currentRow, 1]);
        currentRow++;
        
        // 生成时间
        sheet.Cells[currentRow, 1].Value = $"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        ApplySubtitleStyle(sheet.Cells[currentRow, 1]);
        currentRow++;
        
        // 空行
        currentRow++;
    }
    
    private static void CreateBasicStatistics(ExcelWorksheet sheet, List<ModelIssue> issues, 
                                            Dictionary<IssueType, int> issueStats, 
                                            int totalTriangles, int totalObjects)
    {
        var startRow = 4;
        var currentRow = startRow;
        
        // 统计信息标题
        sheet.Cells[currentRow, 1].Value = "基础统计信息";
        ApplySectionHeaderStyle(sheet.Cells[currentRow, 1, currentRow, 2]);
        currentRow++;
        
        // 统计数据
        var basicStats = new[]
        {
            ("总问题数", issues.Count.ToString()),
            ("问题分类数", issueStats.Count.ToString()),
            ("涉及模型数", issues.Select(i => i.modelName).Distinct().Count().ToString()),
            ("总三角面数", UIDrawer.FormatNumber(totalTriangles)),
            ("总游戏对象", UIDrawer.FormatNumber(totalObjects))
        };
        
        foreach (var (label, value) in basicStats)
        {
            sheet.Cells[currentRow, 1].Value = label;
            sheet.Cells[currentRow, 2].Value = value;
            ApplyDataRowStyle(sheet.Cells[currentRow, 1, currentRow, 2]);
            currentRow++;
        }
        
        // 空行分隔
        currentRow++;
    }
    
    private static void CreateCategoryStatistics(ExcelWorksheet sheet, List<ModelIssue> issues,
                                               Dictionary<IssueType, int> issueStats)
    {
        var startRow = 11;
        var currentRow = startRow;
        
        // 分类统计标题
        sheet.Cells[currentRow, 1].Value = "问题类型分布";
        ApplySectionHeaderStyle(sheet.Cells[currentRow, 1, currentRow, 3]);
        currentRow++;
        
        // 表头
        var headers = new[] { "问题类型", "数量", "占比" };
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[currentRow, i + 1].Value = headers[i];
        }
        ApplyTableHeaderStyle(sheet.Cells[currentRow, 1, currentRow, 3]);
        currentRow++;
        
        // 数据行
        var sheetNames = GetSheetNames(issues);
        foreach (var stat in issueStats.OrderByDescending(s => s.Value))
        {
            var displayName = UIDrawer.GetIssueTypeDisplayName(stat.Key);
            
            // 问题类型名称（带超链接）
            sheet.Cells[currentRow, 1].Value = displayName;
            if (sheetNames.ContainsKey(stat.Key))
            {
                AddHyperlink(sheet.Cells[currentRow, 1], sheetNames[stat.Key], displayName);
            }
            
            // 数量
            sheet.Cells[currentRow, 2].Value = stat.Value;
            
            // 占比
            var percentage = (stat.Value / (float)issues.Count) * 100;
            sheet.Cells[currentRow, 3].Value = $"{percentage:F1}%";
            
            ApplyDataRowStyle(sheet.Cells[currentRow, 1, currentRow, 3]);
            currentRow++;
        }
    }
    
    #endregion
    
    #region 详细表创建
    
    private static void CreateDetailSheets(ExcelPackage package, List<ModelIssue> issues)
    {
        foreach (var group in issues.GroupBy(i => i.type))
        {
            var sheetName = UIDrawer.GetIssueTypeDisplayName(group.Key);
            var sheet = package.Workbook.Worksheets.Add(sheetName);
            
            CreateDetailSheetContent(sheet, group.Key, group.ToList());
        }
    }
    
    private static void CreateDetailSheetContent(ExcelWorksheet sheet, IssueType issueType, 
                                               List<ModelIssue> groupIssues)
    {
        var currentRow = 1;
        
        // 详细页标题
        CreateDetailSheetHeader(sheet, issueType, groupIssues.Count, ref currentRow);
        
        // 表格内容
        CreateDetailTable(sheet, issueType, groupIssues, ref currentRow);
        
        sheet.Cells.AutoFitColumns();
        sheet.View.FreezePanes(5, 1); // 冻结标题行
    }
    
    private static void CreateDetailSheetHeader(ExcelWorksheet sheet, IssueType issueType, 
                                              int issueCount, ref int currentRow)
    {
        var issueTypeName = UIDrawer.GetIssueTypeDisplayName(issueType);
        
        // 页面标题
        sheet.Cells[currentRow, 1].Value = $"问题类型：{issueTypeName}    {TECHNICAL_SUPPORT}";
        ApplyTitleStyle(sheet.Cells[currentRow, 1]);
        currentRow++;
        
        // 问题统计
        sheet.Cells[currentRow, 1].Value = $"问题数量: {issueCount}";
        ApplySubtitleStyle(sheet.Cells[currentRow, 1]);
        currentRow++;
        
        // 生成时间
        sheet.Cells[currentRow, 1].Value = $"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        ApplySubtitleStyle(sheet.Cells[currentRow, 1]);
        currentRow++;
        
        // 空行
        currentRow++;
    }
    
    private static void CreateDetailTable(ExcelWorksheet sheet, IssueType issueType, 
                                        List<ModelIssue> groupIssues, ref int currentRow)
    {
        // 基础表头
        var baseHeaders = new[] { "模型名称", "问题描述", "建议修复" };
        var headers = baseHeaders.ToList();
        
        // 根据问题类型添加特殊列
        if (issueType == IssueType.PivotOffset)
        {
            headers.AddRange(new[] { "当前位置", "偏移之后向量", "偏移距离" });
        }
        
        // 创建表头
        for (int i = 0; i < headers.Count; i++)
        {
            sheet.Cells[currentRow, i + 1].Value = headers[i];
        }
        ApplyTableHeaderStyle(sheet.Cells[currentRow, 1, currentRow, headers.Count]);
        currentRow++;
        
        // 创建数据行
        foreach (var issue in groupIssues)
        {
            CreateDetailDataRow(sheet, issue, headers.Count, currentRow);
            currentRow++;
        }
    }
    
    private static void CreateDetailDataRow(ExcelWorksheet sheet, ModelIssue issue, 
                                          int columnCount, int row)
    {
        // 基础信息
        sheet.Cells[row, 1].Value = issue.modelName;
        sheet.Cells[row, 2].Value = issue.description;
        sheet.Cells[row, 3].Value = issue.suggestion;
        
        // 特殊信息（轴心偏移）
        if (issue.type == IssueType.PivotOffset && issue.pivotOffsetInfo != null)
        {
            var info = issue.pivotOffsetInfo;
            sheet.Cells[row, 4].Value = info.position.ToString();
            sheet.Cells[row, 5].Value = info.offset.ToString();
            sheet.Cells[row, 6].Value = info.distance.ToString("F3");
        }
        
        ApplyDataRowStyle(sheet.Cells[row, 1, row, columnCount]);
    }
    
    #endregion
    
    #region 样式设置
    
    private static void ApplyTitleStyle(ExcelRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.Size = 16;
    }
    
    private static void ApplySubtitleStyle(ExcelRange range)
    {
        range.Style.Font.Size = 11;
    }
    
    private static void ApplySectionHeaderStyle(ExcelRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.Size = 14;
        range.Style.Fill.PatternType = ExcelFillStyle.None;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
    }
    
    private static void ApplyTableHeaderStyle(ExcelRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.Size = 12;
        range.Style.Fill.PatternType = ExcelFillStyle.None;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
    }
    
    private static void ApplyDataRowStyle(ExcelRange range)
    {
        range.Style.Font.Size = 10;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
    }
    
    #endregion
    
    #region 辅助方法
    
    private static Dictionary<IssueType, string> GetSheetNames(List<ModelIssue> issues)
    {
        var sheetNames = new Dictionary<IssueType, string>();
        foreach (var group in issues.GroupBy(i => i.type))
        {
            var sheetName = UIDrawer.GetIssueTypeDisplayName(group.Key);
            sheetNames[group.Key] = sheetName;
        }
        return sheetNames;
    }
    
    private static void AddHyperlink(ExcelRange cell, string targetSheet, string displayText)
    {
        cell.Hyperlink = new ExcelHyperLink($"#{targetSheet}!A1", displayText);
        cell.Style.Font.UnderLine = true;
    }
    
    #endregion
}