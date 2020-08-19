using System;
using System.Collections.Generic;
using System.IO;
using AutoMapper;
using ComposableCollections;
using FluentAssertions;
using LiveLinq.Dictionary;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiveLinq.EntityFramework.Tests
{
    public class TaskDto
    {
        public Guid Id { get; set; }
        public string Description { get; set; }
        public PersonDto AssignedTo { get; set; }
        public Guid? AssignedToId { get; set; }
    }

    public class PersonDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ICollection<TaskDto> AssignedTasks { get; set; }
    }
    
    public class Task {
        public Task(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        public string Description { get; set; }
        public Person AssignedTo { get; set; }
    }

    public class Person
    {
        public Person(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        public string Name { get; set; }
        public List<Task> AssignedTasks { get; } = new List<Task>();
    }

    public class TaskDbContext : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PersonDto>()
                .HasMany(x => x.AssignedTasks)
                .WithOne(x => x.AssignedTo)
                .HasForeignKey(x => x.AssignedToId);

            base.OnModelCreating(modelBuilder);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=tasks.db");
        }
        
        public DbSet<TaskDto> Task { get; set; }
        public DbSet<PersonDto> People { get; set; }
    }

    public class PersonRepository : DelegateObservableDictionaryWithBuiltInKey<Guid, Person>
    {
        public PersonRepository(DatabaseLayer<TaskDbContext> databaseLayer, IMapper mapper)
        {
            var result = databaseLayer.WithAggregateRoot(x => x.People, x => x.Id)
                .WithMapping<Guid, Person, PersonDto>(mapper)
                .WithLiveLinq()
                .WithBuiltInKey(x => x.Id);
            
            Initialize(result);
        }
    }

    public class TaskRepository : DelegateObservableDictionaryWithBuiltInKey<Guid, Task>
    {
        public TaskRepository(DatabaseLayer<TaskDbContext> databaseLayer, IMapper mapper)
        {
            var result = databaseLayer.WithAggregateRoot(x => x.Task, x => x.Id)
                .WithMapping<Guid, Task, TaskDto>(mapper)
                .WithLiveLinq()
                .WithBuiltInKey(x => x.Id);
            
            Initialize(result);
        }
    }

    [TestClass]
    public class DatabaseLayerTests
    {
        [TestMethod]
        public void ShouldHandleManyToOneRelationships()
        {
            var x = Environment.CurrentDirectory;
            if (File.Exists("tasks.db"))
            {
                File.Delete("tasks.db");
            }
            
            var preserveReferencesState = new PreserveReferencesState();
            
            var mapperConfig = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Task, TaskDto>()
                    .ConstructUsing(preserveReferencesState)
                    .ReverseMap()
                    .ConstructUsing(preserveReferencesState, dto => new Task(dto.Id));

                cfg.CreateMap<Person, PersonDto>()
                    .ConstructUsing(preserveReferencesState)
                    .ReverseMap()
                    .ConstructUsing(preserveReferencesState, dto => new Person(dto.Id));
            });

            var mapper = mapperConfig.CreateMapper();
            
            var joeId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            
            using (var databaseLayer = DatabaseLayer.Create(() => new TaskDbContext(), x => x.Database.Migrate()))
            {
                var people = new PersonRepository(databaseLayer, mapper);
                var tasks = new TaskRepository(databaseLayer, mapper);

                var joe = new Person(joeId)
                {
                    Name = "Joe"
                };
                people.Add(joe);

                tasks.Add(new Task(taskId)
                {
                    Description = "Wash the car",
                    AssignedTo = joe
                });
            }

            using (var databaseLayer = DatabaseLayer.Create(() => new TaskDbContext(), x => x.Database.Migrate()))
            {
                var people = new PersonRepository(databaseLayer, mapper);
                var tasks = new TaskRepository(databaseLayer, mapper);

                var joe = people[joeId];
                joe.Name.Should().Be("Joe");
                joe.AssignedTasks.Count.Should().Be(1);
                joe.AssignedTasks[0].Description.Should().Be("Wash the car");

                var washTheCar = tasks[taskId];
                washTheCar.Description.Should().Be("Wash the car");
                ReferenceEquals(washTheCar, joe.AssignedTasks[0]).Should().BeTrue();
            }
        }
    }
}
