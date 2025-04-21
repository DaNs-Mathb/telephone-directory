public class Department{
    public int ID {get;set;}
    public string Name {get; set;} = string.Empty;
    public int? ParentDepartmentId {get;set;}
    public List<Department> Children { get; set; } = new(); 

};
