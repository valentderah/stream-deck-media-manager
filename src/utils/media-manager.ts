import { execFile } from 'child_process';
import path from 'path';

export type MediaInfo = {
	Title?: string;
	Artist?: string;
	Artists?: string[];
	AlbumArtist?: string;
	AlbumTitle?: string;
	Status?: 'Playing' | 'Paused' | 'Stopped';
	CoverArtBase64?: string;
};

export type MediaManagerErrorType = 'FILE_NOT_FOUND' | 'HELPER_ERROR' | 'PARSING_ERROR' | 'NOTHING_PLAYING';

export type MediaManagerError = {
	type: MediaManagerErrorType;
	message: string;
	code?: string | number | null;
};

export type MediaManagerResult = 
	| { success: true; data: MediaInfo }
	| { success: false; error: MediaManagerError };

function getManagerExeName(): string {
	if (process.platform === 'win32') return 'MediaManager.exe';
	return 'MediaManager';
}

function getManagerPath(): string {
	return path.join(process.cwd(), 'bin', getManagerExeName());
}

function createFileNotFoundError(managerPath: string): MediaManagerError {
	const exeName = getManagerExeName();
	console.error(`${exeName} not found at: ${managerPath}`);
	if (process.platform === 'win32') {
		console.error('Please build the C# project using: cd MediaManager/platforms/windows && build.bat');
	} else if (process.platform === 'darwin') {
		console.error('Please build the project using: cd MediaManager/platforms/macos && build.sh');
	} else {
		console.error('Please build the project for your platform');
	}
	return {
		type: 'FILE_NOT_FOUND',
		message: `${exeName} not found at: ${managerPath}`
	};
}

function isMediaInfoEmpty(info: MediaInfo): boolean {
	return !info.Title && !info.Artist && (!info.Artists || info.Artists.length === 0);
}

type ExecuteResult = {
	success: true;
	stdout: string;
	stderr: string;
} | {
	success: false;
	error: MediaManagerError;
};

async function executeManager(args: string[] = []): Promise<ExecuteResult> {
	const managerPath = getManagerPath();

	return new Promise<ExecuteResult>((resolve) => {
		execFile(managerPath, args, (error, stdout, stderr) => {
			if (error) {
				const errorType: MediaManagerErrorType = error.code === 'ENOENT' 
					? 'FILE_NOT_FOUND' 
					: 'HELPER_ERROR';
				
				if (errorType === 'FILE_NOT_FOUND') {
					return resolve({
						success: false,
						error: createFileNotFoundError(managerPath)
					});
				}

				return resolve({
					success: false,
					error: {
						type: errorType,
						message: error.message,
						code: error.code
					}
				});
			}

			if (stderr && !stdout) {
				return resolve({
					success: false,
					error: {
						type: 'HELPER_ERROR',
						message: 'Manager returned stderr'
					}
				});
			}

			return resolve({
				success: true,
				stdout,
				stderr
			});
		});
	});
}

export async function getMediaInfo(): Promise<MediaManagerResult> {
	const result = await executeManager();

	if (!result.success) {
		return result;
	}

	if (!result.stdout || result.stdout.trim().length === 0) {
		return {
			success: false,
			error: {
				type: 'NOTHING_PLAYING',
				message: 'No media is currently playing'
			}
		};
	}

	try {
		const info: MediaInfo = JSON.parse(result.stdout);

		if (isMediaInfoEmpty(info)) {
			return {
				success: false,
				error: {
					type: 'NOTHING_PLAYING',
					message: 'No media data available'
				}
			};
		}

		return {
			success: true,
			data: info
		};
	} catch (parseError) {
		console.error('Failed to parse JSON from manager:', parseError);
		return {
			success: false,
			error: {
				type: 'PARSING_ERROR',
				message: parseError instanceof Error ? parseError.message : 'Failed to parse JSON'
			}
		};
	}
}

export async function toggleMediaPlayPause(): Promise<MediaManagerResult> {
	const result = await executeManager(['toggle']);

	if (!result.success) {
		return result;
	}

	return {
		success: true,
		data: {} as MediaInfo
	};
}

export async function nextMedia(): Promise<MediaManagerResult> {
	const result = await executeManager(['next']);

	if (!result.success) {
		return result;
	}

	return {
		success: true,
		data: {} as MediaInfo
	};
}

export async function previousMedia(): Promise<MediaManagerResult> {
	const result = await executeManager(['previous']);

	if (!result.success) {
		return result;
	}

	return {
		success: true,
		data: {} as MediaInfo
	};
}

