using Npgsql;
using System.Data;



var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddScoped<IDepartmentRepository>(provider => 
    new DepartmentRepository(connectionString));

builder.Services.AddScoped<IEmployeeRepository>(provider => 
    new EmployeeRepository(connectionString));



var app = builder.Build();



if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.MapGet("/",()=>"HELLO WORLD");
app.MapGet("/departments/hierarchy", async (IDepartmentRepository repository) => 
    await repository.GetHierarchyAsync())
    .WithTags("Departments")
    .WithName("GetDepartmentsHierarchy")
    .Produces<IEnumerable<Department>>(200)
    .ProducesProblem(404);

app.MapGet("/departments", async (IDepartmentRepository repository) => 
    await repository.GetAllAsync())
    .WithTags("Departments")
    .WithName("GetAllDepartments")
    .Produces<IEnumerable<Department>>(200);

app.MapGet("/departments/{id}", async (int id, IDepartmentRepository repository) => 
    await repository.GetByIdAsync(id) is Department department
        ? Results.Ok(department)
        : Results.NotFound())
    .WithTags("Departments")
    .WithName("GetDepartmentById")
    .Produces<Department>(200)
    .ProducesProblem(404);

app.MapPost("/departments", async (Department department, IDepartmentRepository repository) =>
    {
        var id = await repository.CreateAsync(department);
        return Results.Created($"/departments/{id}", new { Id = id });
    })
    .WithTags("Departments")
    .WithName("CreateDepartment")
    .Produces<int>(201)
    .ProducesValidationProblem();

app.MapPut("/departments/{id}", async (int id, Department department, IDepartmentRepository repository) =>
    {
        if (id != department.ID)
            return Results.BadRequest("ID in URL and body must match");
        
        return await repository.UpdateAsync(department)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .WithTags("Departments")
    .WithName("UpdateDepartment")
    .Produces(204)
    .ProducesProblem(400)
    .ProducesProblem(404);

app.MapDelete("/departments/{id}", async (int id, IDepartmentRepository repository) =>
    {
        return await repository.DeleteAsync(id)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .WithTags("Departments")
    .WithName("DeleteDepartment")
    .Produces(204)
    .ProducesProblem(404);

app.MapGet("/employees", async (IEmployeeRepository repository) => 
    await repository.GetAllAsync())
    .WithTags("Employees")
    .WithName("GetAllEmployees")
    .Produces<IEnumerable<Employee>>(200);

app.MapGet("/employees/{id}", async (int id, IEmployeeRepository repository) => 
    await repository.GetByIdAsync(id) is Employee employee
        ? Results.Ok(employee)
        : Results.NotFound())
    .WithTags("Employees")
    .WithName("GetEmployeeById")
    .Produces<Employee>(200)
    .ProducesProblem(404);

app.MapPost("/employees", async (Employee employee, IEmployeeRepository repository) =>
    {
        var id = await repository.CreateAsync(employee);
        return Results.Created($"/employees/{id}", new { Id = id });
    })
    .WithTags("Employees")
    .WithName("CreateEmployee")
    .Produces<int>(201)
    .ProducesValidationProblem();

app.MapGet("/employees/search", async (string query, IEmployeeRepository repository) => 
    await repository.SearchAsync(query))
    .WithTags("Employees")
    .WithName("SearchEmployees")
    .Produces<IEnumerable<Employee>>(200);

app.MapPut("/employees/{id}", async (int id, Employee employee, IEmployeeRepository repository) =>
    {
        if (id != employee.ID)
            return Results.BadRequest("ID in URL and body must match");
        
        return await repository.UpdateAsync(employee)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .WithTags("Employees")
    .WithName("UpdateEmployee")
    .Produces(204)
    .ProducesProblem(400)
    .ProducesProblem(404);

app.MapDelete("/employees/{id}", async (int id, IEmployeeRepository repository) =>
    {
        return await repository.DeleteAsync(id)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .WithTags("Employees")
    .WithName("DeleteEmployee")
    .Produces(204)
    .ProducesProblem(404);



app.Run();
