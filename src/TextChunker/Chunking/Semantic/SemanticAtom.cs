namespace TextChunker.Chunking.Semantic;

/// <summary>Тип атома — единицы текста, которую семантическая нарезка считает неделимой.</summary>
public enum SemanticAtomKind
{
    /// <summary>Предложение или его часть.</summary>
    Sentence,

    /// <summary>Заголовок Markdown (<c># …</c>).</summary>
    Heading,

    /// <summary>Элемент списка (<c>- </c>, <c>* </c>, <c>1. </c>).</summary>
    ListItem,

    /// <summary>Блок кода в ограждении (<c>```</c> / <c>~~~</c>), не режется.</summary>
    Code,

    /// <summary>Прочий текст (например, строка без признаков структуры).</summary>
    Text
}

/// <summary>
/// Атом семантической нарезки: <see cref="Text"/> — обрезанное содержимое,
/// <see cref="Start"/>/<see cref="End"/> — его границы в исходном тексте
/// (<see cref="End"/> исключающий, UTF-16 индексы). Атомы упорядочены, не пересекаются;
/// промежутки между ними — разделители (пробелы/переводы строк).
/// </summary>
public sealed record SemanticAtom(string Text, int Start, int End, SemanticAtomKind Kind)
{
    public int Length => End - Start;
}
