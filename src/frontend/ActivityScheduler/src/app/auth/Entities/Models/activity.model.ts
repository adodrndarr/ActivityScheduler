import { DefaultDate } from "./date.model"

export class ActivityEntity {
    constructor(
        public id: string,
        public name: string,
        public imageUrl: string,
        public itemQuantity: number,
        public minUserCount: number,
        public maxUserCount: number,
        public description: string,
        public location: string,
        public imagePath: string
    ) { }
}

export class BookedActivity {
    constructor(
        public startTime: Date,
        public endTime: Date,
        public isValid: boolean,
        public info: string
    ) { }
}

export class ScheduleActivity {
    constructor(
        public bookedForDate: Date,
        public startTime: Date,
        public endTime: Date,
        public activityEntityId: string,
        public userId: string,
        public currentId?: number
    ) { }
}

export class Activity {
    constructor(
        public id: string,
        public name: string,
        public bookedForDate: Date,
        public startTime: Date,
        public endTime: Date,
        public duration: string,
        public activityEntityId: string,
    ) { }
}

export class CreateActivity {
    constructor(
        public defaultDate: DefaultDate,
        public date: Date,
        public activityId: string,
        public userId: string,
        public index: number
    ) { }
}
