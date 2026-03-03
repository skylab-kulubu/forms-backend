using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Contracts.Forms;

public record GetUserFormsRequest(
    int Page = 1,
    int PageSize = 10,
    string? Search = null,               
    CollaboratorRole? Role = null,      
    bool? AllowAnonymous = null,        
    bool? AllowMultiple = null,
    bool? RequiresManualReview = null,         
    bool? HasLinkedForm = null,     
    string SortDirection = "descending"
);