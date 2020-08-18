using System;
using System.Collections.Generic;
using System.IO;
using AutoMapper;
using ComposableCollections;
using FluentAssertions;
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
        public PersonDto()
        {
        }

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
            
            var joeId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            
            var mapperConfig = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<TaskDto, Task>()
                    .PreserveReferences()
                    .ReverseMap();

                cfg.CreateMap<PersonDto, Person>()
                    .PreserveReferences()
                    .ReverseMap();
            });

            var mapper = mapperConfig.CreateMapper();
            
            using (var databaseLayer = DatabaseLayer.Create(() => new TaskDbContext(), x => x.Database.Migrate()))
            {
                var people = databaseLayer.WithAggregateRoot<Guid, Person, PersonDto>(x => x.People, x => x.Id, x => x.Id)
                    .WithBuiltInKey(x => x.Id);
                var tasks = databaseLayer.WithAggregateRoot<Guid, Task, TaskDto>(x => x.Task, x => x.Id, x => x.Id)
                    .WithBuiltInKey(x => x.Id);

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
                var people = databaseLayer.WithAggregateRoot<Guid, Person, PersonDto>(x => x.People, x => x.Id, x => x.Id)
                    .WithBuiltInKey(x => x.Id);
                var tasks = databaseLayer.WithAggregateRoot<Guid, Task, TaskDto>(x => x.Task, x => x.Id, x => x.Id)
                    .WithBuiltInKey(x => x.Id);
            
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
