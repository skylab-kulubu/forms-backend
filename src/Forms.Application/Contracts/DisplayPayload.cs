namespace Forms.Application.Contracts;

public record FormDisplayPayload(
    FormDisplayContract? Form,
    int Step
);