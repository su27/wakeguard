pub const PANEL_WIDTH: i32 = 620;
pub const PANEL_HEIGHT: i32 = 490;

#[derive(Clone, Copy)]
pub struct Bounds {
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
}

pub fn panel_position(work: Bounds, cursor: (i32, i32)) -> (i32, i32) {
    const MARGIN: i32 = 8;
    const CURSOR_GAP: i32 = 12;
    let min_x = work.x + MARGIN;
    let min_y = work.y + MARGIN;
    let max_x = (work.x + work.width - PANEL_WIDTH - MARGIN).max(min_x);
    let max_y = (work.y + work.height - PANEL_HEIGHT - MARGIN).max(min_y);
    let x = (cursor.0 - PANEL_WIDTH + 24).clamp(min_x, max_x);
    let y = if cursor.1 >= work.y + work.height {
        max_y
    } else if cursor.1 < work.y {
        min_y
    } else {
        let above = cursor.1 - PANEL_HEIGHT - CURSOR_GAP;
        (if above >= min_y {
            above
        } else {
            cursor.1 + CURSOR_GAP
        })
        .clamp(min_y, max_y)
    };
    (x, y)
}

#[cfg(test)]
mod tests {
    use super::*;
    const WORK: Bounds = Bounds {
        x: 0,
        y: 0,
        width: 1920,
        height: 1040,
    };

    #[test]
    fn clicks_inside_taskbar_keep_panel_above_work_area_bottom() {
        assert_eq!(panel_position(WORK, (1800, 1060)), (1204, 542));
        assert_eq!(panel_position(WORK, (1919, 1079)), (1292, 542));
    }
    #[test]
    fn opens_near_cursor_and_below_it_when_above_would_not_fit() {
        assert_eq!(panel_position(WORK, (1000, 800)), (404, 298));
        assert_eq!(panel_position(WORK, (1000, 200)), (404, 212));
        assert_eq!(panel_position(WORK, (1000, -10)).1, 8);
    }
    #[test]
    fn supports_negative_monitor_coordinates() {
        let work = Bounds { x: -1920, ..WORK };
        let (x, y) = panel_position(work, (-100, 1060));
        assert!(x >= -1912 && x + PANEL_WIDTH <= -8);
        assert_eq!(y + PANEL_HEIGHT, 1032);
    }
}
