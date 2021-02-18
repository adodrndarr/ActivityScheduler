﻿using ActivityScheduler.Domain.Entities;
using ActivityScheduler.Presentation.EntitiesDTO;
using ActivityScheduler.Services.HelperServices.Interfaces;
using ActivityScheduler.Services.Interfaces;
using AutoMapper;
using Domain.Repositories.Interfaces;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;


namespace ActivityScheduler.Services
{
    public class ActivityService : IActivityService
    {
        public ActivityService(
            IRepositoryContainer repositoryContainer,
            ICommonService helperService,
            IMapper mapper,
            ILoggerManager logger
            )
        {
            _repos = repositoryContainer;
            _mapper = mapper;
            _helperService = helperService;
            _logger = logger;
        }


        private IRepositoryContainer _repos;
        private IMapper _mapper;
        private ICommonService _helperService;
        private ILoggerManager _logger;

        public Activity GetActivityById(Guid activityId)
        {
            var activity = _repos.ActivityRepo.GetActivityById(activityId);

            return activity;
        }

        public void Add(ActivityRequestDTO activityDTO, User user)
        {
            AddSingle(activityDTO, user);
            SaveChanges();
        }

        private void AddSingle(ActivityRequestDTO activityDTO, User user)
        {
            var newActivity = _mapper.Map<Activity>(activityDTO);
            var duration = CalculateDuration(newActivity.StartTime, newActivity.EndTime);
            var activityEntity = _repos.ActivityEntityRepo
                .GetActivityEntityByName(activityDTO.ActivityEntityName);

            newActivity.Name = activityEntity.Name;
            newActivity.OrganizerName = user.UserName;
            newActivity.Duration = duration;
            newActivity.DateBooked = DateTime.Now;
            newActivity.UserId = user.Id;
            newActivity.ActivityEntityId = activityEntity.Id.ToString();

            _repos.ActivityRepo.Create(newActivity);
        }

        public void AddMany(List<ActivityRequestDTO> activityRequestDTOs, User user)
        {
            activityRequestDTOs.ForEach(a => AddSingle(a, user));
            SaveChanges();
        }

        public void Update(Activity activityToUpdate, ActivityRequestDTO newActivity)
        {
            activityToUpdate.Name = newActivity.ActivityEntityName;
            activityToUpdate.BookedForDate = newActivity.BookedForDate;
            activityToUpdate.StartTime = newActivity.StartTime;
            activityToUpdate.EndTime = newActivity.EndTime;
            activityToUpdate.OrganizerName = newActivity.OrganizerName;
            activityToUpdate.Duration = CalculateDuration(activityToUpdate.StartTime, activityToUpdate.EndTime);

            var activityEntity = _repos.ActivityEntityRepo
                .GetActivityEntityByName(newActivity.ActivityEntityName);

            var idToUpdate = activityToUpdate.ActivityEntityId;
            idToUpdate = (activityEntity == null) ? idToUpdate : activityEntity.Id.ToString();

            activityToUpdate.ActivityEntityId = idToUpdate;

            _repos.ActivityRepo.Update(activityToUpdate);
            SaveChanges();
        }

        public ResultDetails Delete(Guid activityId)
        {
            _logger.LogInfo("ActivityService Delete - Getting specific activity to delete...");
            var activityToRemove = GetActivityById(activityId);

            if (activityToRemove != null)
            {
                _repos.ActivityRepo.Delete(activityToRemove);
                SaveChanges();

                _logger.LogInfo("ActivityService Delete - Deletion was successful.");
                return new ResultDetails
                {
                    StatusCode = StatusCodes.Status200OK,
                    IsSuccessful = true,
                    Info = $"The activity has been deleted."
                };
            }

            _logger.LogInfo("ActivityService Delete - Deletion failed.");
            return new ResultDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                IsSuccessful = false,
                Info = $"The activity with id: {activityId}, could not be deleted."
            };
        }

        public void SaveChanges()
        {
            _repos.SaveChanges();
        }
        
        public ResultDetails ScheduleActivity(User user, ActivityRequestDTO newActivityDTO)
        {
            UpdateActivityTime(newActivityDTO);
            if (user != null && newActivityDTO.BookedForDate > DateTime.Now)
            {
                ActivityEntity activityEntity = GetActivityEntity(newActivityDTO);

                if (activityEntity == null)
                {
                    _logger.LogInfo("ActivityService ScheduleActivity - Scheduling of an activity failed.");
                    return new ResultDetails
                    {
                        StatusCode = StatusCodes.Status400BadRequest,
                        IsSuccessful = false,
                        Info = $"Scheduling of an activity failed, the activity entity couldn't be found."
                    };
                }

                newActivityDTO.ActivityEntityName = activityEntity.Name;
                newActivityDTO.OrganizerName = user.UserName;

                var bookedActivities = GetBookedActivities(activityEntity);
                var isTimeSlotAvailable = IsScheduleTimeAvailable(newActivityDTO, bookedActivities);

                if (isTimeSlotAvailable)
                {
                    Add(newActivityDTO, user);
                    SaveChanges();

                    _logger.LogInfo("ActivityService ScheduleActivity - Scheduling of an activity was successful.");
                    return new ResultDetails
                    {
                        StatusCode = StatusCodes.Status201Created,
                        IsSuccessful = true,
                        Info = $"The activity {newActivityDTO?.ActivityEntityName}, has been scheduled."
                    };
                }

                _logger.LogInfo("ActivityService ScheduleActivity - Scheduling of an activity failed.");
                return new ResultDetails
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    IsSuccessful = false,
                    Info = $"Scheduling failed, there is already an activity booked for that period or the time is invalid."
                };
            }

            _logger.LogInfo("ActivityService ScheduleActivity - Scheduling of an activity failed.");
            return new ResultDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                IsSuccessful = false,
                Info = $"Please provide and check to have a valid name, date and id."
            };
        }

        public ScheduleResponseDTO ScheduleActivities(User user, List<ActivityRequestDTO> newActivityDTOs)
        {
            var schedulingResponse = new ScheduleResponseDTO
            {
                Info = $"Scheduling finished successfully.",
                UnsuccessfulInfo = new List<string>()
            };

            for (int i = 0; i < newActivityDTOs.Count; i++)
            {
                var activity = newActivityDTOs[i];
                var scheduleProcess = ScheduleActivity(user, activity);

                if (!scheduleProcess.IsSuccessful)
                {
                    schedulingResponse.UnsuccessfulInfo.Add($"No. {i + 1} - {scheduleProcess.Info}\n");
                }
            }

            _logger.LogInfo("ActivityService ScheduleActivities - Scheduling of activities finished.");
            return schedulingResponse;
        }

        public ResultDetails CheckAvailability(ActivityEntity activityEntity, DateTime forDate)
        {
            if (activityEntity != null)
            {
                if (forDate.Date < DateTime.Now.Date)
                {
                    return new ResultDetails
                    {
                        StatusCode = StatusCodes.Status200OK,
                        Payload = new BookedActivityDTO
                        {
                            IsValid = false,
                            Info = "Invalid date, provided date must not be in the past."
                        }
                    };
                }

                var newActivity = _repos.ActivityRepo.GetByCondition(a => a.ActivityEntityId ==
                                                                          activityEntity.Id.ToString())
                                                     .FirstOrDefault();
                var newActivityDTO = _mapper.Map<ActivityRequestDTO>(newActivity);

                if (newActivityDTO != null)
                {
                    var bookedActivities = GetBookedActivities(activityEntity);
                    if (bookedActivities.Count > 0)
                    {
                        var bookedDetails = _mapper.Map<IEnumerable<BookedActivityDTO>>(bookedActivities);
                        foreach (var details in bookedDetails)
                        {
                            details.IsValid = true;
                            details.Info = "Booked for the given time periods.";
                        }

                        return new ResultDetails
                        {
                            StatusCode = StatusCodes.Status200OK,
                            Payload = bookedDetails
                        };
                    }

                    return new ResultDetails
                    {
                        StatusCode = StatusCodes.Status200OK,
                        Payload = new BookedActivityDTO
                        {
                            IsValid = true,
                            Info = "Free to book anytime, there are no booked times for this date."
                        }
                    };
                }
            }

            return new ResultDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Payload = new BookedActivityDTO
                {
                    IsValid = false,
                    Info = "No such activity entity found, re-check the provided id."
                }
            };
        }

        public string CalculateDuration(DateTime startTime, DateTime endTime)
        {
            var duration = _helperService.CalculateDuration(startTime, endTime);

            return duration;
        }

        public bool ActivityEntityExists(string name)
        {
            var activityEntity = _repos.ActivityEntityRepo.GetActivityEntityByName(name);
            var exists = (activityEntity == null) ? false : true;
            
            return exists;
        }

        public bool IsScheduleTimeAvailable(ActivityRequestDTO newActivityDTO, List<Activity> bookedActivities)
        {
            bool noBookedActivities = bookedActivities.Count < 1;
            if (noBookedActivities)
            {
                return true;
            }

            bool canBeBooked = false;
            foreach (var bookedActivity in bookedActivities)
            {
                int newActivityStartHour = newActivityDTO.StartTime.Hour;
                int newActivityEndHour = newActivityDTO.EndTime.Hour;

                int bookedActivityStartHour = bookedActivity.StartTime.Hour;
                int bookedActivityEndHour = bookedActivity.EndTime.Hour;
                int bookedActivityEndMinute = bookedActivity.EndTime.Minute;

                bool endsBeforeBookedActivity = newActivityEndHour <= bookedActivityStartHour;
                bool startsAfterBookedActivity = newActivityStartHour > bookedActivityEndHour;
                bool startsAfterBookedActivityAndMinutes = newActivityStartHour >= bookedActivityEndHour &&
                                                           bookedActivityEndMinute == 0;

                bool hasValidStartAndEndTimes = bookedActivityStartHour < bookedActivityEndHour;

                if (hasValidStartAndEndTimes && (endsBeforeBookedActivity ||
                                                 startsAfterBookedActivity ||
                                                 startsAfterBookedActivityAndMinutes)
                   )
                {
                    canBeBooked = true;
                }
                else
                {
                    canBeBooked = false;
                }
            }

            return canBeBooked;
        }

        public List<Activity> GetBookedActivities(ActivityEntity activityEntity)
        {
            var allActivitites = _repos.ActivityRepo.GetByCondition(a => a.Name.ToLower() == 
                                                                         activityEntity.Name.ToLower())
                                                    .ToList();

            var bookedActivities = new List<Activity>();
            var bookedQuantity = 0;

            for (int i = 0; i < allActivitites.Count; i++)
            {
                var currentActivity = allActivitites[i];
                var currentDate = currentActivity.BookedForDate.Date;
                var currentStartHour = currentActivity.StartTime.Hour;
                var currentEndhour = currentActivity.EndTime.Hour;

                var alreadyBooked = bookedActivities
                    .Any(bookedActivity => bookedActivity.BookedForDate.Date == currentDate &&
                                           bookedActivity.StartTime.Hour == currentStartHour &&
                                           bookedActivity.EndTime.Hour == currentEndhour);
                if (alreadyBooked)
                {
                    continue;
                }

                foreach (var activity in allActivitites)
                {
                    var activityDate = activity.BookedForDate.Date;
                    var activityStartHour = activity.StartTime.Hour;
                    var activityEndHour = activity.EndTime.Hour;

                    bool activityExists = activityDate == currentDate &&
                                          activityStartHour == currentStartHour &&
                                          activityEndHour == currentEndhour;
                    if (activityExists)
                    {
                        bookedQuantity += 1;

                        bool allQuantitiesAreTaken = bookedQuantity >= activityEntity.ItemQuantity;
                        if (allQuantitiesAreTaken)
                        {
                            bookedActivities.Add(activity);
                            bookedQuantity = 0;
                        }
                    }
                }

                bookedQuantity = 0;
            }

            return bookedActivities;
        }

        private void UpdateActivityTime(ActivityRequestDTO activity)
        {
            activity.BookedForDate = activity.BookedForDate.ToLocalTime();
            activity.StartTime = activity.BookedForDate;
            activity.EndTime = activity.BookedForDate.AddHours(1);
        }

        private ActivityEntity GetActivityEntity(ActivityRequestDTO newActivityDTO)
        {
            bool activityEntityExists = ActivityEntityExists(newActivityDTO.ActivityEntityName);
            ActivityEntity activityEntity = null;

            if (newActivityDTO?.ActivityEntityId != null)
            {
                activityEntity = _repos.ActivityEntityRepo
                    .GetActivityEntityById(new Guid(newActivityDTO.ActivityEntityId));
            }

            if (activityEntity == null && activityEntityExists)
            {
                activityEntity = _repos.ActivityEntityRepo
                    .GetActivityEntityByName(newActivityDTO.ActivityEntityName);
            }

            return activityEntity;
        }
    }
}
