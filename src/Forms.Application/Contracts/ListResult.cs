public record ListResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize
)
{
  public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);  
}