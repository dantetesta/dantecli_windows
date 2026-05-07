using System;
using System.IO;

namespace DanteCLI.Models;

public enum EditorLanguage
{
    Plain, Swift, Python, JavaScript, TypeScript, Ruby, Go, Rust, Java, Kotlin,
    Php, Bash, Markdown, Json, Yaml, Toml, Html, Css, Sql, Xml, Dockerfile, Makefile,
    CSharp, Cpp
}

public static class EditorLanguageExtensions
{
    public static EditorLanguage Detect(string fileUrl)
    {
        var name = Path.GetFileName(fileUrl).ToLowerInvariant();
        switch (name)
        {
            case "dockerfile": return EditorLanguage.Dockerfile;
            case "makefile": case "gnumakefile": return EditorLanguage.Makefile;
            case ".zshrc": case ".bashrc": case ".bash_profile":
            case ".profile": case ".zprofile": return EditorLanguage.Bash;
        }
        var ext = Path.GetExtension(fileUrl).ToLowerInvariant().TrimStart('.');
        return ext switch
        {
            "swift" => EditorLanguage.Swift,
            "py" => EditorLanguage.Python,
            "js" or "jsx" or "mjs" or "cjs" => EditorLanguage.JavaScript,
            "ts" or "tsx" => EditorLanguage.TypeScript,
            "rb" => EditorLanguage.Ruby,
            "go" => EditorLanguage.Go,
            "rs" => EditorLanguage.Rust,
            "java" => EditorLanguage.Java,
            "kt" or "kts" => EditorLanguage.Kotlin,
            "php" => EditorLanguage.Php,
            "sh" or "bash" or "zsh" or "fish" or "ps1" or "psm1" => EditorLanguage.Bash,
            "md" or "markdown" or "mdx" => EditorLanguage.Markdown,
            "json" or "jsonc" => EditorLanguage.Json,
            "yml" or "yaml" => EditorLanguage.Yaml,
            "toml" => EditorLanguage.Toml,
            "html" or "htm" => EditorLanguage.Html,
            "css" or "scss" or "sass" => EditorLanguage.Css,
            "sql" => EditorLanguage.Sql,
            "xml" or "plist" or "manifest" or "csproj" or "vbproj" => EditorLanguage.Xml,
            "cs" => EditorLanguage.CSharp,
            "cpp" or "cc" or "cxx" or "h" or "hpp" => EditorLanguage.Cpp,
            _ => EditorLanguage.Plain,
        };
    }

    public static string Label(this EditorLanguage l) => l switch
    {
        EditorLanguage.Plain => "Plain Text",
        EditorLanguage.Bash => "Shell",
        EditorLanguage.JavaScript => "JavaScript",
        EditorLanguage.TypeScript => "TypeScript",
        EditorLanguage.CSharp => "C#",
        EditorLanguage.Cpp => "C++",
        _ => l.ToString(),
    };

    /// <summary>AvalonEdit highlight definition name (built-in set).</summary>
    public static string AvalonHighlight(this EditorLanguage l) => l switch
    {
        EditorLanguage.CSharp => "C#",
        EditorLanguage.Cpp => "C++",
        EditorLanguage.JavaScript or EditorLanguage.TypeScript => "JavaScript",
        EditorLanguage.Java => "Java",
        EditorLanguage.Php => "PHP",
        EditorLanguage.Python => "Python",
        EditorLanguage.Ruby => "Ruby",
        EditorLanguage.Bash => "Bash",
        EditorLanguage.Markdown => "MarkDown",
        EditorLanguage.Html => "HTML",
        EditorLanguage.Css => "CSS",
        EditorLanguage.Json => "Json",
        EditorLanguage.Yaml or EditorLanguage.Toml => "YAML",
        EditorLanguage.Sql => "TSQL",
        EditorLanguage.Xml => "XML",
        _ => "",
    };
}
