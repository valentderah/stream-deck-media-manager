import { action, KeyDownEvent, SingletonAction, WillAppearEvent } from '@elgato/streamdeck';
import { previousMedia } from '../utils/media-manager';

@action({ UUID: 'ru.valentderah.media-manager.media-previous' })
export class MediaPreviousAction extends SingletonAction {
	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await previousMedia();
	}
}

