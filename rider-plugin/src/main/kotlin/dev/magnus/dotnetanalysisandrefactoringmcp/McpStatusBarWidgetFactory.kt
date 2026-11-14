package dev.magnus.dotnetanalysisandrefactoringmcp

import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.StatusBarWidget
import com.intellij.openapi.wm.StatusBarWidgetFactory

class McpStatusBarWidgetFactory : StatusBarWidgetFactory {
    override fun getId(): String = "DotNetAnalysisAndRefactoringMcp.StatusBar"
    override fun getDisplayName(): String = ".NET Analysis&Refactoring MCP"
    override fun isAvailable(project: Project): Boolean = true
    override fun createWidget(project: Project): StatusBarWidget = McpStatusBarWidget()
    override fun isEnabledByDefault(): Boolean = true
}
